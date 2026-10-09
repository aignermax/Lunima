using System.Net;
using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Services.Update;
using CAP.Avalonia.ViewModels.Settings;
using CAP.Avalonia.ViewModels.Update;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.Views;
using CAP_Core.Update;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Opt-in artifact capture for the issue-#1160 PR: renders the real SettingsWindow in
/// German on the two affected pages — "Raster &amp; Ausrichtung" (whose description used
/// to clip at the right edge) and "Software-Updates" (whose "Current: v…" label was
/// hardcoded English) — and writes PNGs + manifest.json to
/// <c>artifacts/ui-screenshots/issue-1160/</c>. No-op unless <c>UI_SHOT_DIR</c> is set,
/// so CI runs it as an instant pass.
/// </summary>
[Collection("LocalizationSingleton")]
public class Issue1160SettingsScreenshotTests
{
    [Trait("Category", "UiScreenshots")]
    [AvaloniaFact]
    public void CaptureGermanSettingsPages()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return;
        var outputDir = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDir);

        var previous = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.German.Code);
        Window? window = null;
        try
        {
            var updateVm = CreateOfflineUpdateViewModel();
            var vm = new SettingsWindowViewModel(new ISettingsPage[]
            {
                new TestPage("Settings.Section.GridAlignment",
                    new GridSnapSettingsViewModel(new GridSnapSettings(), new AlignmentGuideViewModel())),
                new TestPage("Settings.Section.SoftwareUpdates", updateVm),
            });
            window = new SettingsWindow { DataContext = vm };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            SaveFrame(window, Path.Combine(outputDir, "01-grid-snap-de.png"));

            updateVm.CurrentVersionText.ShouldStartWith("Aktuell: v");
            vm.SelectedPage = vm.Pages[1];
            Dispatcher.UIThread.RunJobs();
            SaveFrame(window, Path.Combine(outputDir, "02-updates-de.png"));

            ScreenshotArtifacts.WriteText(Path.Combine(outputDir, "manifest.json"), Manifest);
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    /// <summary>An UpdateViewModel that never reaches the network — the capture only
    /// needs the localized "Aktuell: v…" label, not a live release check.</summary>
    private static UpdateViewModel CreateOfflineUpdateViewModel()
    {
        var httpClient = new HttpClient(new OfflineHandler());
        return new UpdateViewModel(
            new UpdateChecker(httpClient, "owner", "repo"),
            new UpdateDownloader(httpClient),
            new UserPreferencesService(Path.GetTempFileName()),
            new NoOpUrlLauncher(),
            new NoOpInstaller());
    }

    private static void SaveFrame(Window window, string path)
    {
        var bitmap = window.CaptureRenderedFrame();
        bitmap.ShouldNotBeNull("render miss for settings-page capture");
        using (bitmap)
        {
            var bytes = ScreenshotArtifacts.SavePng(bitmap!, path);
            bytes.Length.ShouldBeGreaterThan(0);
        }
    }

    /// <summary>Repo-root <c>artifacts/ui-screenshots/issue-1160</c> (or <c>UI_SHOT_DIR/issue-1160</c>).</summary>
    private static string ResolveOutputDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (!string.IsNullOrEmpty(envDir))
            return Path.Combine(envDir, "issue-1160");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1160");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1160");
    }

    private const string Manifest = """
        [
          {"file": "01-grid-snap-de.png", "caption": "Issue #1160: German 'Raster & Ausrichtung' settings page — the guide description now wraps inside the content pane instead of clipping at the right window edge."},
          {"file": "02-updates-de.png", "caption": "Issue #1160: German 'Software-Updates' settings page — the version label is localized ('Aktuell: v…') instead of hardcoded English."}
        ]
        """;

    /// <summary>Settings page with a real localized title (so the sidebar shows the German
    /// section name) and an ASCII icon — headless frames render emoji as tofu boxes.</summary>
    private sealed class TestPage : LocalizedSettingsPage
    {
        public TestPage(string titleKey, object viewModel)
            : base(titleKey, LocalizationService.Instance) => ViewModel = viewModel;

        public override string Icon => "-";
        public override object ViewModel { get; }
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(""),
            });
    }

    private sealed class NoOpUrlLauncher : IUrlLauncher
    {
        public void Open(string url) { }
        public void OpenFileOrDirectory(string path) { }
        public void RevealInFileManager(string path) { }
    }

    private sealed class NoOpInstaller : IInstaller
    {
        public bool CanInstallInPlace(out string reason)
        {
            reason = "screenshot capture: in-place update disabled";
            return false;
        }

        public void LaunchUpdater(string downloadedArchivePath) { }
    }
}
