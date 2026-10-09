using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.BusView;
using CAP.Avalonia.Views;
using CAP.Avalonia.Views.Panels;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Logic-panel re-homing regression + visual documentation (issue #1183, rest of
/// #1156): the Logic panel — whole-design build → toggle → step/run clock →
/// timeline/waveforms — lives as the Logic tab of the bottom analysis dock, and
/// the right sidebar keeps selection-driven panels only. The structural facts are
/// asserted unconditionally; PNGs + manifest.json land in
/// <c>artifacts/ui-screenshots/issue-1183/</c> when UI_SHOT_DIR is set.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1183LogicDockScreenshotTests
{
    private const int DockWindowWidth = 1200;
    private const int DockWindowHeight = 700;
    private const int CaptureAttempts = 3;
    private const int LogicTabIndex = 8;

    /// <summary>The analysis dock hosts the re-homed Logic tab.</summary>
    [AvaloniaFact]
    public void AnalysisDock_HostsLogicTab()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.BottomPanel.Analysis.IsVisible = true;
        var dock = new AnalysisDockPanel { DataContext = vm };
        var window = new Window { Width = DockWindowWidth, Height = DockWindowHeight, Content = dock };
        window.Show();
        try
        {
            vm.BottomPanel.Analysis.SelectedTabIndex = LogicTabIndex;
            Dispatcher.UIThread.RunJobs();
            dock.GetVisualDescendants().OfType<LogicPanel>()
                .Any(p => p.IsEffectivelyVisible)
                .ShouldBeTrue("the Logic panel is not the visible analysis tab at index 8.");
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>The right sidebar no longer hosts the Logic panel.</summary>
    [AvaloniaFact]
    public void RightSidebar_DoesNotHostLogicPanel()
    {
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            var rightPanel = window.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => b.Name == "RightPanelBorder");
            rightPanel.ShouldNotBeNull();
            rightPanel.GetVisualDescendants().OfType<LogicPanel>().ShouldBeEmpty();
            rightPanel.GetVisualDescendants().OfType<TruthTablePanel>().ShouldHaveSingleItem(
                "the Truth Table panel acts on the selected gate group and stays in the sidebar");
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Captures the dock Logic tab with the Counter example built, before and after three clock steps.</summary>
    [AvaloniaFact]
    public async Task CaptureLogicDockTab()
    {
        // Opt-in like UiScreenshotTests: only runs when screenshots are explicitly requested.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return;
        var outputDir = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDir);
        foreach (var stale in Directory.GetFiles(outputDir, "*.png"))
            File.Delete(stale);

        // The shipped 2-bit counter, loaded and built through the real panel path.
        var counterPath = Path.Combine(
            ExampleDesignFilesTests.ExamplesDirectory(), "Logic Gate Counter 2-bit.lun");
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(counterPath);
        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        var logic = vm.RightPanel.Logic;
        await logic.BuildNetworkCommand.ExecuteAsync(null);
        logic.HasNetwork.ShouldBeTrue(logic.StatusText);

        vm.BottomPanel.Analysis.IsVisible = true;
        vm.BottomPanel.Analysis.SetDockHeight(560);
        var dock = new AnalysisDockPanel { DataContext = vm };
        var dockWindow = new Window { Width = DockWindowWidth, Height = DockWindowHeight, Content = dock };
        dockWindow.Show();
        try
        {
            vm.BottomPanel.Analysis.OpenLogic();
            Dispatcher.UIThread.RunJobs();
            CaptureWithRetry(dockWindow, Path.Combine(outputDir, "01-dock-logic-tab-built.png"));

            // Resting state of the active-low preset toggle is on — the counter counts.
            logic.Inputs.Single(i => i.PinName == "S̄").IsOn = true;
            for (var step = 0; step < 3; step++)
                logic.StepClockCommand.Execute(null);
            logic.ClockStepCount.ShouldBe(3);
            logic.OutputRows.OfType<LogicSignalBusOutputViewModel>().Single(b => b.Prefix == "C")
                .DecimalValue.ShouldBe(3, "three clock steps count C1C0 from 00 to 11");

            // Scroll inside the tab to the timeline and waveform lanes for the capture.
            var logicPanel = dock.GetVisualDescendants().OfType<LogicPanel>().Single();
            var scroller = logicPanel.GetVisualAncestors().OfType<ScrollViewer>().First();
            Dispatcher.UIThread.RunJobs();
            scroller.Offset = new Avalonia.Vector(0, scroller.Extent.Height);
            Dispatcher.UIThread.RunJobs();
            CaptureWithRetry(dockWindow, Path.Combine(outputDir, "02-dock-logic-tab-after-3-steps.png"));
        }
        finally
        {
            dockWindow.Close();
            Dispatcher.UIThread.RunJobs();
        }

        WriteManifest(outputDir);
        Directory.GetFiles(outputDir, "*.png").Length.ShouldBe(2);
    }

    private static void WriteManifest(string outputDir)
    {
        const string manifest = """
        [
          {"file": "01-dock-logic-tab-built.png", "caption": "Logic panel re-homed as the Logic tab of the bottom analysis dock — the 2-bit counter example's network is built, inputs/outputs/register state visible before Run."},
          {"file": "02-dock-logic-tab-after-3-steps.png", "caption": "Same dock Logic tab after three Step-clock presses (scrolled inside the tab): the counter counted 1 → 2 → 3, the timeline grew behind '── clock #k ──' dividers and the waveform lanes show the committed bits."}
        ]
        """;
        ScreenshotArtifacts.WriteText(Path.Combine(outputDir, "manifest.json"), manifest);
    }

    /// <summary>Repo-root <c>artifacts/ui-screenshots/issue-1183</c> (or <c>UI_SHOT_DIR/issue-1183</c>).</summary>
    private static string ResolveOutputDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (!string.IsNullOrEmpty(envDir))
            return Path.Combine(envDir, "issue-1183");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1183");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1183");
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
