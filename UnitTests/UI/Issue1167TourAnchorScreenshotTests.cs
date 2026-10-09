using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls.TourAnchor;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.Views;
using CAP_Core.Components.Process;
using CAP.Avalonia.Views.Panels;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the anchored guided tours (#1167): boots the real
/// MainWindow around the shipped 2-bit counter example and captures every
/// spotlighted step of the first-steps and watch-it-compute tours, plus the
/// no-target fallback card. PNGs + manifest.json land in
/// <c>artifacts/ui-screenshots/issue-1167/</c> when UI_SHOT_DIR is set.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1167TourAnchorScreenshotTests
{
    private const double DockHeight = 380;
    private const int CaptureAttempts = 5;

    /// <summary>Captures all seven anchored-tour frames in one MainWindow session.</summary>
    [AvaloniaFact]
    public async Task CaptureTourAnchorFrames()
    {
        // Opt-in like UiScreenshotTests: only runs when screenshots are explicitly requested.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return;
        var outputDir = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDir);
        foreach (var stale in Directory.GetFiles(outputDir, "*.png"))
            File.Delete(stale);

        var counterPath = Path.Combine(
            ExampleDesignFilesTests.ExamplesDirectory(), "Logic Gate Counter 2-bit.lun");
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(counterPath);
        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        vm.FileOperations.SetActiveProcess(ActiveProcessSelection.Playground(), markDirty: false);

        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            vm.Home.IsHomeVisible = false;
            Dispatcher.UIThread.RunJobs();
            FitDesign(window, vm);

            var overlays = window.GetVisualDescendants().OfType<TourAnchorOverlay>().ToList();
            var firstSteps = overlays.Single(o => o.Card is FirstStepsTutorialPanel);
            var watch = overlays.Single(o => o.Card is WatchComputeTourPanel);

            // First-steps tour: the engine is driven manually (the loaded design would
            // auto-complete the place/connect steps), the overlay anchoring is real.
            vm.Tutorial.IsActive = true;
            CaptureTourStep(window, firstSteps, () => vm.Tutorial.CurrentStepIndex = 0,
                vm.Tutorial.Steps[0].TargetName, Path.Combine(outputDir, "01-firststeps-library.png"));
            CaptureTourStep(window, firstSteps, () => vm.Tutorial.CurrentStepIndex = 1,
                vm.Tutorial.Steps[1].TargetName, Path.Combine(outputDir, "02-firststeps-canvas.png"));
            CaptureTourStep(window, firstSteps, () => vm.Tutorial.CurrentStepIndex = 2,
                vm.Tutorial.Steps[2].TargetName, Path.Combine(outputDir, "03-firststeps-run-simulation.png"));
            vm.Tutorial.IsActive = false;

            // Watch-it-compute tour: dock open on the Logic tab, real panel state.
            vm.BottomPanel.Analysis.IsVisible = true;
            vm.BottomPanel.Analysis.SetDockHeight(DockHeight);
            vm.BottomPanel.Analysis.OpenLogic();
            Dispatcher.UIThread.RunJobs();

            vm.WatchTour.IsActive = true;
            CaptureTourStep(window, watch, () => vm.WatchTour.CurrentStepIndex = 0,
                vm.WatchTour.Steps[0].TargetName, Path.Combine(outputDir, "04-watchtour-build.png"));

            await vm.RightPanel.Logic.BuildNetworkCommand.ExecuteAsync(null);
            vm.RightPanel.Logic.HasNetwork.ShouldBeTrue(vm.RightPanel.Logic.StatusText);
            ScrollLogicPanelToRegisters(window);
            CaptureTourStep(window, watch, () => vm.WatchTour.CurrentStepIndex = 1,
                vm.WatchTour.Steps[1].TargetName, Path.Combine(outputDir, "05-watchtour-step-clock.png"));

            vm.RightPanel.Logic.StepClockCommand.Execute(null);
            ScrollLogicPanelToRegisters(window);
            CaptureTourStep(window, watch, () => vm.WatchTour.CurrentStepIndex = 2,
                vm.WatchTour.Steps[2].TargetName, Path.Combine(outputDir, "06-watchtour-run.png"));

            // Final step names no target: the card falls back to bottom-centre, no dim.
            CaptureTourStep(window, watch, () => vm.WatchTour.CurrentStepIndex = 4,
                targetName: null, Path.Combine(outputDir, "07-no-target-fallback.png"));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        WriteManifest(outputDir);
        Directory.GetFiles(outputDir, "*.png").Length.ShouldBe(7);
    }

    /// <summary>
    /// Advances the engine, then re-applies the target name so the overlay recomputes
    /// after layout (and after the BringIntoView scroll a first application may cause) —
    /// in the app the 150 ms poll timer does this; headless tests drive it explicitly.
    /// </summary>
    private static void CaptureTourStep(
        Window window, TourAnchorOverlay overlay, Action advance, string? targetName, string path)
    {
        advance();
        Dispatcher.UIThread.RunJobs();
        overlay.TargetName = targetName;
        // Let the BringIntoView scroll settle (it needs a render/layout pass)
        // before recomputing the spotlight from the final target position.
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            using var settleFrame = window.CaptureRenderedFrame();
        }
        overlay.TargetName = null;
        Dispatcher.UIThread.RunJobs();
        overlay.TargetName = targetName;
        SaveFrame(window, path);
    }

    /// <summary>
    /// Scrolls the dock's Logic tab to its bottom so the register row (Step clock /
    /// Run) is on screen — headless, the overlay's BringIntoView lands a frame late.
    /// </summary>
    private static void ScrollLogicPanelToRegisters(Window window)
    {
        var logicPanel = window.GetVisualDescendants().OfType<LogicPanel>()
            .First(p => p.IsEffectivelyVisible);
        var scroller = logicPanel.GetVisualAncestors().OfType<ScrollViewer>().First();
        Dispatcher.UIThread.RunJobs();
        scroller.Offset = new global::Avalonia.Vector(0, scroller.Extent.Height);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Fits the loaded design into the canvas viewport (zoom + pan).</summary>
    private static void FitDesign(MainWindow window, MainViewModel vm)
    {
        var components = vm.Canvas.Components;
        if (components.Count == 0)
            return;
        double minX = components.Min(c => c.X);
        double minY = components.Min(c => c.Y);
        double maxX = components.Max(c => c.X + c.Width);
        double maxY = components.Max(c => c.Y + c.Height);
        ShowcaseCircuit.SetView(window, vm, (minX, minY, maxX - minX, maxY - minY));
    }

    private static void SaveFrame(Window window, string path)
    {
        Bitmap? bitmap = null;
        for (var attempt = 0; attempt < CaptureAttempts; attempt++)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            var frame = window.CaptureRenderedFrame();
            if (frame == null)
                continue;
            (bitmap as IDisposable)?.Dispose();
            bitmap = frame;
        }
        bitmap.ShouldNotBeNull($"render miss for {Path.GetFileName(path)}");
        using (bitmap)
            ScreenshotArtifacts.SavePng(bitmap!, path).Length.ShouldBeGreaterThan(0);
    }

    private static void WriteManifest(string outputDir)
    {
        const string manifest = """
        [
          {"file": "01-firststeps-library.png", "caption": "First-steps tour step 1/3: the component library is spotlighted (dim hole + pulsing outline + arrow) and the card docks beside it instead of floating."},
          {"file": "02-firststeps-canvas.png", "caption": "Step 2/3 anchors to the design canvas: the dim layer frees the whole drawing surface while the card keeps out of its way."},
          {"file": "03-firststeps-run-simulation.png", "caption": "Step 3/3 points at the Run-Simulation toolbar button, with the card placed below it so the target stays uncovered."},
          {"file": "04-watchtour-build.png", "caption": "Watch-it-compute tour step 1/5: the Build button inside the analysis dock's Logic tab is spotlighted with the counter example loaded."},
          {"file": "05-watchtour-step-clock.png", "caption": "Step 2/5 anchors to the Step-clock button after the network was built; the overlay scrolled the register row into view first."},
          {"file": "06-watchtour-run.png", "caption": "Step 3/5 targets the Run button in the same register row, one manual clock step already on the timeline."},
          {"file": "07-no-target-fallback.png", "caption": "A step that names no target falls back to the legacy bottom-centre card with no dimming — the whole UI stays visible."}
        ]
        """;
        ScreenshotArtifacts.WriteText(Path.Combine(outputDir, "manifest.json"), manifest);
    }

    /// <summary>Repo-root <c>artifacts/ui-screenshots/issue-1167</c> (or <c>UI_SHOT_DIR/issue-1167</c>).</summary>
    private static string ResolveOutputDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (!string.IsNullOrEmpty(envDir))
            return Path.Combine(envDir, "issue-1167");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1167");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1167");
    }
}
