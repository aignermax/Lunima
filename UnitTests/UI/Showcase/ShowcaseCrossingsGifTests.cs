using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels;
using CAP_Core.Routing.CrossingInsertion;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.Views;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.UI.Flows;
using Xunit;

namespace UnitTests.UI.Showcase;

/// <summary>
/// Feature showcase: <c>crossings.gif</c>. Two Demo-PDK wires cross through a crossing; the
/// Cut tool cuts the wire coming out of it (a second crossing drops in, the halves keep
/// their exact geometry), a third wire is routed straight through the new crossing, and
/// finally the first crossing is dragged along its horizontal wire with the real mouse —
/// the horizontal pieces only stretch, just the two perpendicular pieces re-route.
/// Opt-in via <c>UI_SHOT_DIR</c>; the GIF lands in <c>UI_SHOT_DIR/v0.17/</c>.
/// </summary>
[Trait("Category", "Showcase")]
[Collection("LocalizationSingleton")]
public class ShowcaseCrossingsGifTests
{
    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;
    private const int SceneHoldMs = 800;
    private const int CutArmedMs = 600;
    private const int CutMs = 800;
    private const int ThirdWireMs = 800;
    private const int DragStepMs = 140;
    private const int DragEndHoldMs = 1300;
    private const double DragDistance = -70;
    private const int DragSteps = 6;
    private const int RenderPasses = 3;
    private static readonly TimeSpan RoutingTimeout = TimeSpan.FromSeconds(30);

    [AvaloniaFact]
    public async Task CaptureCrossings()
    {
        if (!ShowcaseCapture.Enabled) return;
        await ShowcaseCapture.WithEnglishUiAsync(async () =>
        {
            var vm = ShowcaseCircuit.CreateStagedViewModel();
            var window = ShowcaseCircuit.BootMainWindow(vm, WindowWidth, WindowHeight);
            UiInput.Descendants<Avalonia.Controls.Border>(window).First(b => b.Name == "RightPanelBorder").IsVisible = false;
            var scene = await ShowcaseCrossingsScene.BuildAsync(vm);
            await ShowcaseCircuit.WaitForRoutingIdleAsync(vm.Canvas);
            ShowcaseCircuit.SetView(window, vm, ShowcaseCrossingsScene.Extent);
            var canvasControl = ShowcaseFraming.Canvas(window);

            var gif = new ShowcaseGifRecorder("crossings", window, Framing(window, vm));
            gif.Capture(SceneHoldMs);

            // Cut armed, pointer resting on the candidate where the waiting coupler's guide
            // meets the wire coming out of the crossing.
            vm.CanvasInteraction.SetCutModeCommand.Execute(null);
            var cutPoint = CanvasPoint(window, vm, ShowcaseCrossingsScene.SecondWireX, ShowcaseCrossingsScene.WireY);
            window.MouseMove(cutPoint);
            ForceRenderPasses(canvasControl);
            gif.Capture(CutArmedMs);
            UiInput.ClickAt(window, cutPoint);
            await ShowcaseCircuit.WaitForRoutingIdleAsync(vm.Canvas);
            var cut = Crossings(vm).Single(c => c != scene.Crossing);
            vm.CanvasInteraction.SetSelectModeCommand.Execute(null);
            vm.CanvasInteraction.SelectedComponent = vm.Canvas.Components.First(c => c.Component == cut);
            Dispatcher.UIThread.RunJobs();
            gif.Capture(CutMs);

            vm.CanvasInteraction.SelectedComponent = null;
            var (north, south) = FreePorts(vm, cut);
            await ShowcaseCrossingsScene.Connect(vm, ShowcaseCrossingsScene.Waveguide(scene.SecondTop), north);
            await ShowcaseCrossingsScene.Connect(vm, south, ShowcaseCrossingsScene.Waveguide(scene.SecondBottom));
            await ShowcaseCircuit.WaitForRoutingIdleAsync(vm.Canvas);
            gif.Capture(ThirdWireMs);

            await DragAlongWireAsync(window, vm, gif, scene.Crossing);
            gif.Capture(DragEndHoldMs);

            vm.Canvas.Connections.ShouldAllBe(c => c.Connection.IsPathValid && !c.Connection.IsBlockedFallback,
                "no showcase frame may end on an unrouted wire");
            await gif.EncodeAsync();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    /// <summary>Presses on the crossing, moves it along the horizontal wire in steps
    /// (one frame each, showing the axis guide and ghost) and releases.</summary>
    private static async Task DragAlongWireAsync(MainWindow window, MainViewModel vm, ShowcaseGifRecorder gif, Component crossing)
    {
        var (cx, cy) = ShowcaseCrossingsScene.CrossingCenter;
        double startX = crossing.PhysicalX;
        var from = CanvasPoint(window, vm, cx, cy);
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        Point to = from;
        for (int step = 1; step <= DragSteps; step++)
        {
            to = CanvasPoint(window, vm, cx + DragDistance * step / DragSteps, cy);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            gif.Capture(DragStepMs);
        }
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        await WaitForRoutesAsync(vm);
        crossing.PhysicalY.ShouldBe(ShowcaseCrossingsScene.WireY - crossing.HeightMicrometers / 2, 1e-6,
            "the drag moves the crossing along its horizontal wire only");
        (crossing.PhysicalX - startX).ShouldBe(DragDistance, 2.0);
    }

    private static async Task WaitForRoutesAsync(MainViewModel vm)
    {
        var clock = Stopwatch.StartNew();
        while (vm.Canvas.Connections.Any(c => c.Connection.RoutedPath == null) || vm.Canvas.IsRouting)
        {
            clock.Elapsed.ShouldBeLessThan(RoutingTimeout, "the re-routed pieces never got a route");
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }
        await ShowcaseCircuit.WaitForRoutingIdleAsync(vm.Canvas);
    }

    /// <summary>The cut crossing's two undocked ports, upper one first.</summary>
    private static (PhysicalPin North, PhysicalPin South) FreePorts(MainViewModel vm, Component crossing)
    {
        var docked = vm.Canvas.Connections
            .SelectMany(c => new[] { c.Connection.StartPin, c.Connection.EndPin }).ToHashSet();
        var free = crossing.PhysicalPins.Where(p => !docked.Contains(p))
            .OrderBy(p => p.GetAbsolutePosition().y).ToList();
        free.Count.ShouldBe(2, "a cut leaves the new crossing's perpendicular ports free");
        return (free[0], free[1]);
    }

    private static List<Component> Crossings(MainViewModel vm) =>
        vm.Canvas.Components.Select(c => c.Component)
            .Where(CrossingComponentCatalog.IsCrossing).ToList();

    /// <summary>The canvas from its left edge, toolbar strip down to below the scene — the
    /// workspace bar and the armed tool's mode pill stay in the picture.</summary>
    private static PixelRect Framing(MainWindow window, MainViewModel vm)
    {
        var canvas = ShowcaseCapture.BoundsIn(window, ShowcaseFraming.Canvas(window));
        var scene = ShowcaseFraming.CanvasWorld(window, vm, ShowcaseCrossingsScene.Extent);
        return new PixelRect(canvas.X, 0, canvas.Width, scene.Bottom);
    }

    private static Point CanvasPoint(MainWindow window, MainViewModel vm, double x, double y)
    {
        var canvasControl = ShowcaseFraming.Canvas(window);
        return canvasControl.TranslatePoint(
            new Point(x * canvasControl.Zoom + vm.Canvas.PanX, y * canvasControl.Zoom + vm.Canvas.PanY), window)!.Value;
    }

    /// <summary>Pumps render ticks so the Cut overlay computes its candidates before the click.</summary>
    private static void ForceRenderPasses(DesignCanvas canvasControl)
    {
        for (int i = 0; i < RenderPasses; i++)
        {
            canvasControl.InvalidateVisual();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
