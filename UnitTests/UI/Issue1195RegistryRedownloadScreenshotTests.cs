using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services.ComponentRegistry;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.ComponentRegistry.RegistryBrowser;
using CAP.Avalonia.Views;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Shouldly;
using UnitTests.ComponentRegistry.RegistryClient;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Renders the registry browser's download status line for issue #1195: the
/// first download reports "Added …", a re-download of the same registry entry
/// reports "Replaced … — previous version backed up to .trash". PNGs are
/// written to <c>docs/pr-media/issue-1195/</c> for PR review embedding.
/// </summary>
/// <remarks>
/// Run with: <c>dotnet test UnitTests/UnitTests.csproj --filter FullyQualifiedName~Issue1195</c>
/// </remarks>
[Trait("Category", "UiWalkthrough")]
// Renders the full registry browser window as Skia PNGs — CI-only (local runners
// exclude Category=Slow; run explicitly via the filter above).
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class Issue1195RegistryRedownloadScreenshotTests : IDisposable
{
    private const int WindowWidth = 1100;
    private const int WindowHeight = 720;

    private readonly string _storeRoot = Path.Combine(
        Path.GetTempPath(), "lunima-issue-1195-shots", Guid.NewGuid().ToString("N"));

    /// <summary>Pins English so the captured status text is locale-independent.</summary>
    public Issue1195RegistryRedownloadScreenshotTests() =>
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

    public void Dispose()
    {
        if (Directory.Exists(_storeRoot))
            Directory.Delete(_storeRoot, recursive: true);
    }

    [AvaloniaFact]
    public void CaptureFirstDownloadAndRedownloadMessages()
    {
        var outputDir = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDir);
        foreach (var stale in Directory.GetFiles(outputDir, "*.png"))
            File.Delete(stale);

        using var harness = new RegistryTestHarness();
        var client = harness.CreateClient();
        var store = new UserPdkStore(_storeRoot, new PdkJsonSaver(), new PdkLoader());
        var registry = new RegistryBrowserViewModel(client, new RegistryDownloadService(client, store));

        var window = new RegistryBrowserWindow
        {
            Width = WindowWidth,
            Height = WindowHeight,
            DataContext = registry,
        };
        window.Show();
        PumpUntilComplete(registry.IndexLoadTask);
        PumpUntilComplete(registry.PreviewsLoadTask);
        registry.SelectedComponent = registry.Components.First(c => c.Id == "y-branch-1x2");
        PumpUntilComplete(registry.DetailsLoadTask);

        // First download of the entry: "Added …".
        registry.DownloadCommand.Execute(null);
        PumpUntilComplete(registry.DownloadTask);
        registry.DownloadMessage.ShouldContain("Added");
        Capture(window, outputDir, "01-first-download-added.png");

        // Re-download of the SAME entry: the new "Replaced … backed up to .trash" message.
        registry.DownloadCommand.Execute(null);
        PumpUntilComplete(registry.DownloadTask);
        registry.DownloadMessage.ShouldContain("Replaced");
        registry.DownloadMessage.ShouldContain(".trash");
        Capture(window, outputDir, "02-redownload-replaced.png");

        window.Close();
        Dispatcher.UIThread.RunJobs();

        foreach (var entry in Directory.GetFiles(outputDir, "*.png"))
            new FileInfo(entry).Length.ShouldBeGreaterThan(0, $"Screenshot must not be empty: {entry}");
        Directory.GetFiles(outputDir, "*.png").Length.ShouldBe(2);
    }

    /// <summary>Renders the current state of the shown window and saves it as a PNG.</summary>
    private static void Capture(Window window, string outputDir, string filename)
    {
        Dispatcher.UIThread.RunJobs();
        using var bitmap = window.CaptureRenderedFrame();
        bitmap.ShouldNotBeNull($"CaptureRenderedFrame returned null for {filename}");
        bitmap.Save(Path.Combine(outputDir, filename));
    }

    /// <summary>Pumps the headless dispatcher until the async ViewModel load completes.</summary>
    private static void PumpUntilComplete(Task task)
    {
        while (!task.IsCompleted)
            Dispatcher.UIThread.RunJobs();
        task.GetAwaiter().GetResult();
    }

    /// <summary>Repo-root <c>docs/pr-media/issue-1195</c> — only with <c>CAP_UPDATE_PR_MEDIA=1</c>; otherwise a temp dir.</summary>
    private static string ResolveOutputDirectory() =>
        ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1195");
}
