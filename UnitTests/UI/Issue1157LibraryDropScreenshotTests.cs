using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Behaviors;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Panels;
using Shouldly;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Library drag&amp;drop (issue #1157): dragging a template from the Component Library
/// (or a saved group) onto the canvas drops the instance at the release point and returns
/// to Select mode — before the fix the release did nothing and the app stayed stuck in
/// "[P] Platzieren" mode. The structural facts are asserted unconditionally; PNGs +
/// manifest.json land in <c>artifacts/ui-screenshots/issue-1157/</c> when UI_SHOT_DIR is set.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1157LibraryDropScreenshotTests
{
    private const int CaptureAttempts = 3;

    /// <summary>The library list boxes are drag sources; the design canvas is a drop target.</summary>
    [AvaloniaFact]
    public void LibraryListBoxes_AreDragSources_AndCanvas_IsDropTarget()
    {
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        vm.Home.IsHomeVisible = false;
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            var listBoxes = window.GetVisualDescendants().OfType<ListBox>().ToList();
            listBoxes.Count(l => LibraryDragBehavior.GetIsEnabled(l)).ShouldBe(3,
                "component library + user groups + PDK groups must all be drag sources");

            var canvas = window.GetVisualDescendants().OfType<DesignCanvas>().First();
            DragDrop.GetAllowDrop(canvas).ShouldBeTrue("the canvas must accept library drops");
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Dropping a template places it at the release point and disarms place mode.</summary>
    [AvaloniaFact]
    public void DropComponentTemplate_PlacesAtReleasePoint_AndReturnsToSelectMode()
    {
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        vm.Home.IsHomeVisible = false;
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            var template = vm.LeftPanel.AllTemplates
                .First(t => t.PdkSource == ShowcaseCircuit.PdkName && t.Name == "2x2 MMI Coupler");

            vm.CanvasInteraction.DropComponentTemplateAt(template, 800, 400);
            Dispatcher.UIThread.RunJobs();

            var placed = vm.Canvas.Components.ShouldHaveSingleItem(
                "the drop must place the dragged template at the release point");
            placed.Component.WidthMicrometers.ShouldBe(template.WidthMicrometers);
            placed.X.ShouldBe(800 - template.WidthMicrometers / 2, 0.001);
            placed.Y.ShouldBe(400 - template.HeightMicrometers / 2, 0.001);

            vm.CanvasInteraction.CurrentMode.ShouldBe(InteractionMode.Select,
                "after a drop the canvas must not stay stuck in place mode");
            vm.CanvasInteraction.SelectedTemplate.ShouldBeNull();
            vm.StatusText.ShouldContain("Placed");
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Captures the drag ghost hovering the canvas and the dropped result.</summary>
    [AvaloniaFact]
    public void CaptureLibraryDrop()
    {
        // Opt-in like UiScreenshotTests: only runs when screenshots are explicitly requested.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return;
        var outputDir = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDir);
        foreach (var stale in Directory.GetFiles(outputDir, "*.png"))
            File.Delete(stale);

        var vm = ShowcaseCircuit.CreateStagedViewModel();
        vm.Home.IsHomeVisible = false;
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            var canvas = window.GetVisualDescendants().OfType<DesignCanvas>().First();
            var template = vm.LeftPanel.AllTemplates
                .First(t => t.PdkSource == ShowcaseCircuit.PdkName && t.Name == "2x2 MMI Coupler");

            // Mid-drag: the ghost follows the pointer over the canvas, the status bar
            // tells the user that releasing drops the component.
            vm.CanvasInteraction.SelectedTemplate = template;
            canvas.InteractionState.ShowPlacementPreview = true;
            canvas.InteractionState.PlacementPreviewTemplate = template;
            canvas.InteractionState.PlacementPreviewPosition = new Point(800, 400);
            vm.StatusText = string.Format(
                LocalizationService.Instance.Translate("Status.DragDropPlace"), template.Name);
            canvas.InvalidateVisual();
            Dispatcher.UIThread.RunJobs();
            CaptureWithRetry(window, Path.Combine(outputDir, "01-drag-ghost-over-canvas.png"));

            // Release: the instance sits at the release point, place mode is disarmed.
            canvas.InteractionState.ResetPlacementPreview();
            vm.CanvasInteraction.DropComponentTemplateAt(template, 800, 400);
            Dispatcher.UIThread.RunJobs();
            canvas.InvalidateVisual();
            Dispatcher.UIThread.RunJobs();
            CaptureWithRetry(window, Path.Combine(outputDir, "02-after-drop.png"));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        WriteManifest(outputDir);
        Directory.GetFiles(outputDir, "*.png").Length.ShouldBe(2);
    }

    private static void WriteManifest(string outputDir)
    {
        const string manifest = """
        [
          {"file": "01-drag-ghost-over-canvas.png", "caption": "Mid-drag: the 2x2 MMI Coupler ghost follows the pointer over the canvas and the status bar reads 'Release to place' — before the fix, releasing here did nothing."},
          {"file": "02-after-drop.png", "caption": "After the release: the instance sits at the release point and the canvas is back in Select mode — no second click needed."}
        ]
        """;
        ScreenshotArtifacts.WriteText(Path.Combine(outputDir, "manifest.json"), manifest);
    }

    /// <summary>Repo-root <c>artifacts/ui-screenshots/issue-1157</c> (or <c>UI_SHOT_DIR/issue-1157</c>).</summary>
    private static string ResolveOutputDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (!string.IsNullOrEmpty(envDir))
            return Path.Combine(envDir, "issue-1157");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1157");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1157");
    }

    /// <summary>Captures the window, pumping the dispatcher and keeping the last good frame.</summary>
    private static void CaptureWithRetry(Window window, string path)
    {
        WriteableBitmap? bitmap = null;
        for (int attempt = 0; attempt < CaptureAttempts; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            var frame = window.CaptureRenderedFrame();
            if (frame == null)
                continue;
            bitmap?.Dispose();
            bitmap = frame;
        }

        bitmap.ShouldNotBeNull($"CaptureRenderedFrame stayed null after {CaptureAttempts} attempts for {path}");
        using (bitmap)
        {
            ScreenshotArtifacts.SavePng(bitmap, path);
        }
    }
}
