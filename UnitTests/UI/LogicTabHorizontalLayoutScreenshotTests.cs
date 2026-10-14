using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.Views.Panels;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// The Logic tab lives in the wide, short bottom analysis dock, so its layout is
/// horizontal: one action toolbar row on top and the content sections (inputs,
/// outputs + registers, timeline, waveforms) side by side in independently
/// scrolling columns. The structural facts are asserted unconditionally; PNGs land
/// in <c>docs/pr-media/logic-tab-horizontal/</c> when <c>CAP_UPDATE_PR_MEDIA=1</c>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class LogicTabHorizontalLayoutScreenshotTests
{
    private const int WideWidth = 1600;
    private const int NarrowWidth = 900;
    private const int VeryNarrowWidth = 700;
    private const int DockWindowHeight = 300;
    private const double DockContentHeight = 260;
    private const int CaptureAttempts = 3;
    private const int CounterSteps = 3;
    private const double RowTolerance = 4;
    private const string MediaFolder = "logic-tab-horizontal";

    /// <summary>At a typical dock size the sections sit side by side on one row, not stacked.</summary>
    [AvaloniaFact]
    public async Task WideDock_SectionsAreSideBySide()
    {
        var vm = await BuildCounterAfterSteps();
        var window = ShowDock(vm, WideWidth);
        try
        {
            var inputs = Named(window, "LogicInputRows");
            var outputs = Named(window, "LogicOutputRows");
            var timeline = Named(window, "LogicTimelineEvents");
            var inputsTop = TopLeft(window, inputs);
            var outputsTop = TopLeft(window, outputs);
            var timelineTop = TopLeft(window, timeline);

            outputsTop.X.ShouldBeGreaterThan(inputsTop.X + inputs.Bounds.Width - 1);
            timelineTop.X.ShouldBeGreaterThan(outputsTop.X + outputs.Bounds.Width - 1);
            Math.Abs(outputsTop.Y - inputsTop.Y).ShouldBeLessThan(RowTolerance);
            Math.Abs(timelineTop.Y - inputsTop.Y).ShouldBeLessThan(RowTolerance);
        }
        finally
        {
            Close(window);
        }
    }

    /// <summary>The build and clock actions share one toolbar row above the columns.</summary>
    [AvaloniaFact]
    public async Task WideDock_ActionsShareOneToolbarRow()
    {
        var vm = await BuildCounterAfterSteps();
        var window = ShowDock(vm, WideWidth);
        try
        {
            var build = TopLeft(window, Named(window, "LogicBuildButton"));
            var step = TopLeft(window, Named(window, "LogicStepClockButton"));
            var run = TopLeft(window, Named(window, "LogicRunStopButton"));
            var inputs = TopLeft(window, Named(window, "LogicInputRows"));

            Math.Abs(step.Y - build.Y).ShouldBeLessThan(RowTolerance);
            Math.Abs(run.Y - build.Y).ShouldBeLessThan(RowTolerance);
            inputs.Y.ShouldBeGreaterThan(build.Y);
        }
        finally
        {
            Close(window);
        }
    }

    /// <summary>At a narrow dock every toolbar action stays fully inside the window.</summary>
    [AvaloniaFact]
    public async Task NarrowDock_ToolbarActionsAreNotClipped()
    {
        var vm = await BuildCounterAfterSteps();
        var window = ShowDock(vm, NarrowWidth);
        try
        {
            foreach (var name in new[] { "LogicBuildButton", "LogicStepClockButton", "LogicRunStopButton" })
            {
                var control = Named(window, name);
                var topLeft = TopLeft(window, control);
                (topLeft.X + control.Bounds.Width).ShouldBeLessThanOrEqualTo(NarrowWidth, name);
                (topLeft.Y + control.Bounds.Height).ShouldBeLessThanOrEqualTo(DockWindowHeight, name);
            }
            var columnsScroller = Named(window, "LogicColumns").GetVisualAncestors().OfType<ScrollViewer>().First();
            columnsScroller.Extent.Width.ShouldBeLessThanOrEqualTo(columnsScroller.Viewport.Width + 1,
                "at a 900 px dock the columns fit side by side without horizontal scrolling");
        }
        finally
        {
            Close(window);
        }
    }

    /// <summary>Captures the Logic tab (AND 4-bit and 2-bit counter) at a wide and a narrow dock.</summary>
    [AvaloniaFact]
    public async Task CaptureLogicTab()
    {
        if (Environment.GetEnvironmentVariable(ScreenshotArtifacts.UpdatePrMediaVariable) != "1")
            return;
        var outputDir = ScreenshotArtifacts.ResolvePrMediaDirectory(MediaFolder);
        Directory.CreateDirectory(outputDir);

        var and4 = await BuildExample("Logic Gate AND 4-bit.lun");
        and4.RightPanel.Logic.Inputs.First().IsOn = true;
        Capture(and4, WideWidth, Path.Combine(outputDir, "after-and4-1600x300.png"));
        Capture(and4, NarrowWidth, Path.Combine(outputDir, "after-and4-900x300.png"));

        var counter = await BuildCounterAfterSteps();
        Capture(counter, WideWidth, Path.Combine(outputDir, "after-counter-1600x300.png"));
        Capture(counter, NarrowWidth, Path.Combine(outputDir, "after-counter-900x300.png"));
        Capture(counter, VeryNarrowWidth, Path.Combine(outputDir, "after-counter-700x300.png"));

        var halfAdder = await BuildExample("Logic Gate Half Adder.lun");
        halfAdder.RightPanel.Logic.HasFanOutWarnings.ShouldBeTrue("the half adder documents the fan-out column");
        halfAdder.RightPanel.Logic.Inputs.First().IsOn = true;
        Capture(halfAdder, WideWidth, Path.Combine(outputDir, "after-half-adder-fanout-1600x300.png"));
    }

    private static async Task<MainViewModel> BuildCounterAfterSteps()
    {
        var vm = await BuildExample("Logic Gate Counter 2-bit.lun");
        var logic = vm.RightPanel.Logic;
        logic.Inputs.Single(i => i.PinName == "S̄").IsOn = true;
        for (var step = 0; step < CounterSteps; step++)
            logic.StepClockCommand.Execute(null);
        return vm;
    }

    private static async Task<MainViewModel> BuildExample(string fileName)
    {
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), fileName);
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(path);
        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        await vm.RightPanel.Logic.BuildNetworkCommand.ExecuteAsync(null);
        vm.RightPanel.Logic.HasNetwork.ShouldBeTrue(vm.RightPanel.Logic.StatusText);
        vm.BottomPanel.Analysis.IsVisible = true;
        vm.BottomPanel.Analysis.SetDockHeight(DockContentHeight);
        return vm;
    }

    private static Window ShowDock(MainViewModel vm, int width)
    {
        var dock = new AnalysisDockPanel { DataContext = vm };
        var window = new Window { Width = width, Height = DockWindowHeight, Content = dock };
        window.Show();
        vm.BottomPanel.Analysis.OpenLogic();
        Dispatcher.UIThread.RunJobs();
        ScreenshotArtifacts.SettleAnimations();
        return window;
    }

    private static Control Named(Window window, string name)
    {
        var control = window.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => c.Name == name && c.IsEffectivelyVisible);
        control.ShouldNotBeNull($"no visible control named {name} in the Logic tab");
        return control;
    }

    private static Point TopLeft(Window window, Control control) =>
        control.TranslatePoint(new Point(0, 0), window) ?? throw new InvalidOperationException(control.Name);

    private static void Capture(MainViewModel vm, int width, string path)
    {
        var window = ShowDock(vm, width);
        try
        {
            WriteableBitmap? bitmap = null;
            for (var attempt = 0; attempt < CaptureAttempts; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                var frame = window.CaptureRenderedFrame();
                if (frame == null)
                    continue;
                bitmap?.Dispose();
                bitmap = frame;
            }
            bitmap.ShouldNotBeNull($"CaptureRenderedFrame stayed null for {path}");
            using (bitmap)
                ScreenshotArtifacts.SavePng(bitmap, path);
        }
        finally
        {
            Close(window);
        }
    }

    private static void Close(Window window)
    {
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
