using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Views;
using CAP.Avalonia.Views.Panels;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Headless screenshot harness — renders key Avalonia Views offscreen via Skia and writes PNGs
/// to <c>artifacts/ui-screenshots/</c> in the repo root for downstream QA visual inspection.
/// </summary>
/// <remarks>
/// Run with: <c>dotnet test UnitTests/UnitTests.csproj --filter Category=UiScreenshots</c>
/// Output directory override: set env var <c>UI_SHOT_DIR</c> to an absolute path.
/// </remarks>
[Trait("Category", "UiScreenshots")]
public class UiScreenshotTests
{
    /// <summary>
    /// Captures all target Views in one pass. Uses a single MainViewModel so panel bindings
    /// that navigate through RightPanel/LeftPanel sub-properties resolve correctly.
    /// Each panel is wrapped in its own Window; failures are caught and logged so one bad
    /// panel does not block the rest.
    /// </summary>
    [AvaloniaFact]
    public void CaptureAllUiScreenshots()
    {
        var outputDir = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDir);

        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var captured = new List<string>();
        var skipped = new List<(string Name, string Reason)>();

        // All panels use x:DataType="vm:MainViewModel", so pass the full VM as DataContext.
        TryCapture(() => new MainView(), vm, 1280, 900, outputDir, "MainView.png", captured, skipped);
        TryCapture(() => new DesignChecksPanel(), vm, 450, 700, outputDir, "DesignChecksPanel.png", captured, skipped);
        TryCapture(() => new AiAssistantPanel(), vm, 450, 800, outputDir, "AiAssistantPanel.png", captured, skipped);
        TryCapture(() => new LayoutCompressionPanel(), vm, 450, 600, outputDir, "LayoutCompressionPanel.png", captured, skipped);
        TryCapture(() => new RoutingDiagnosticsPanel(), vm, 600, 700, outputDir, "RoutingDiagnosticsPanel.png", captured, skipped);
        TryCapture(() => new SelectedComponentPropertiesPanel(), vm, 450, 600, outputDir, "SelectedComponentPropertiesPanel.png", captured, skipped);

        foreach (var (name, reason) in skipped)
            Console.WriteLine($"[SKIPPED] {name}: {reason}");

        foreach (var path in captured)
            Console.WriteLine($"[OK] {path} ({new FileInfo(path).Length:N0} bytes)");

        foreach (var path in captured)
        {
            var info = new FileInfo(path);
            info.Exists.ShouldBeTrue($"Screenshot file must exist: {path}");
            // 2 KB min: a fully blank 450×600 solid-color PNG compresses to ~600 bytes,
            // so >2 KB confirms the renderer produced real pixels with actual content.
            info.Length.ShouldBeGreaterThan(2000,
                $"Screenshot must be non-trivial (>2 KB) — a blank/empty render means " +
                $"UseHeadlessDrawing was not set to false or UseSkia() is missing: {path}");
        }

        captured.Count.ShouldBeGreaterThan(0, "At least one screenshot must be captured");
    }

    private static void TryCapture(
        Func<Control> createView,
        object dataContext,
        int width,
        int height,
        string outputDir,
        string filename,
        List<string> captured,
        List<(string, string)> skipped)
    {
        try
        {
            var view = createView();
            view.DataContext = dataContext;

            var window = new Window
            {
                Width = width,
                Height = height,
                Content = view
            };

            window.Show();
            Dispatcher.UIThread.RunJobs();

            var bitmap = window.CaptureRenderedFrame();
            window.Close();

            if (bitmap == null)
            {
                skipped.Add((filename, "CaptureRenderedFrame returned null — likely a render miss"));
                return;
            }

            var path = Path.Combine(outputDir, filename);
            using (bitmap)
            {
                bitmap.Save(path);
            }

            captured.Add(path);
        }
        catch (Exception ex)
        {
            skipped.Add((filename, $"{ex.GetType().Name}: {ex.Message}"));
        }
    }

    /// <summary>
    /// Resolves the output directory. Checks <c>UI_SHOT_DIR</c> env var first, then walks up
    /// from the test binary to find the repo root (directory containing a <c>.sln</c> file).
    /// </summary>
    private static string ResolveOutputDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (!string.IsNullOrEmpty(envDir))
            return envDir;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots");
            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots");
    }
}
