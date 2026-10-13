using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CAP.Avalonia.Controls;
using CAP_Core.Components.Connections;
using Shouldly;
using UnitTests.Routing.CrossingInsertion;
using UnitTests.Routing.CrossingInsertion.CrossingMove;
using Xunit;

namespace UnitTests.UI.Flows;

/// <summary>
/// User story for sliding a crossing along its wire: in Select mode the user drags a
/// router-placed crossing mostly to the right — it moves along the horizontal wire only, the
/// horizontal pieces keep their geometry with stretched/shrunk end straights, only the
/// vertical pieces are re-routed, the crossing becomes user-owned, and undo/redo replay it.
/// </summary>
[Trait("Category", "UiFlows")]
// Boots the real MainWindow through the input pipeline — too heavy for local default
// runs (CI covers it, the local runners exclude Category=Slow).
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class UiFlowCrossingAxisDragTests
{
    private const double DragX = 40;
    private const double DragYJitter = 6;
    private static readonly TimeSpan RoutingTimeout = TimeSpan.FromSeconds(30);

    [AvaloniaFact]
    public void DraggingACrossingSideways_MovesItAlongTheHorizontalWire_AndReroutesOnlyTheVerticalPieces()
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        var win = host.Window;
        var scene = CrossedWiresScene.Build(vm.Canvas, routerPlaced: true);
        UiInput.RunJobs();
        var canvasControl = UiInput.Descendants<DesignCanvas>(win).First();
        Point CanvasPoint(double x, double y) =>
            canvasControl.TranslatePoint(
                new Point(x * canvasControl.Zoom + vm.Canvas.PanX, y * canvasControl.Zoom + vm.Canvas.PanY), win)!.Value;

        var before = scene.Pieces.ToDictionary(p => p, CrossedWiresScene.Geometry);
        double startX = scene.Crossing.PhysicalX, startY = scene.Crossing.PhysicalY;
        double center = CrossedWiresScene.Center;

        UiInput.DragMouse(win, CanvasPoint(center, center), CanvasPoint(center + DragX, center + DragYJitter));
        WaitForRoutes(scene.North, scene.South);

        scene.Crossing.PhysicalY.ShouldBe(startY, 1e-9, "a mostly horizontal drag never moves the crossing vertically");
        double moved = scene.Crossing.PhysicalX - startX;
        moved.ShouldBe(DragX, 1.0, "the crossing follows the pointer along the horizontal wire");
        scene.Crossing.IsInsertedCrossing.ShouldBeFalse("a crossing the user moved is user-owned");

        AssertPatched(scene.West, before[scene.West], endMovedBy: moved);
        AssertPatched(scene.East, before[scene.East], startMovedBy: moved);
        foreach (var vertical in new[] { scene.North, scene.South })
        {
            CrossedWiresScene.Geometry(vertical).ShouldNotBe(before[vertical], "the vertical pieces are re-routed");
            vertical.IsPathValid.ShouldBeTrue();
        }

        vm.CommandManager.Undo().ShouldBeTrue();
        WaitForRoutes(scene.North, scene.South);
        scene.Crossing.PhysicalX.ShouldBe(startX, 1e-9);
        scene.Crossing.IsInsertedCrossing.ShouldBeTrue();
        foreach (var piece in scene.Pieces)
            CrossedWiresScene.Geometry(piece).ShouldBe(before[piece], "undo restores every piece");

        vm.CommandManager.Redo().ShouldBeTrue();
        WaitForRoutes(scene.North, scene.South);
        scene.Crossing.PhysicalX.ShouldBe(startX + moved, 1e-9);
        AssertPatched(scene.West, before[scene.West], endMovedBy: moved);
    }

    [AvaloniaFact]
    public void ShiftDraggingACrossing_MovesItFreely()
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        var win = host.Window;
        var scene = CrossedWiresScene.Build(vm.Canvas);
        UiInput.RunJobs();
        var canvasControl = UiInput.Descendants<DesignCanvas>(win).First();
        Point CanvasPoint(double x, double y) =>
            canvasControl.TranslatePoint(
                new Point(x * canvasControl.Zoom + vm.Canvas.PanX, y * canvasControl.Zoom + vm.Canvas.PanY), win)!.Value;
        double startX = scene.Crossing.PhysicalX, startY = scene.Crossing.PhysicalY;
        double center = CrossedWiresScene.Center;

        var from = CanvasPoint(center, center);
        var to = CanvasPoint(center + DragX, center + DragX);
        win.MouseMove(from, RawInputModifiers.Shift);
        win.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
        win.MouseMove(to, RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
        win.MouseUp(to, MouseButton.Left, RawInputModifiers.Shift);
        UiInput.RunJobs();

        (scene.Crossing.PhysicalX - startX).ShouldBeGreaterThan(DragX / 2);
        (scene.Crossing.PhysicalY - startY).ShouldBeGreaterThan(DragX / 2, "Shift frees the drag from the wire axes");
    }

    /// <summary>Opt-in (UI_SHOT_DIR): captures the drag feedback — axis guide and ghost — mid-drag.</summary>
    [AvaloniaFact]
    public void CaptureDragPreview()
    {
        var shotDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (string.IsNullOrEmpty(shotDir)) return;
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        var win = host.Window;
        CrossedWiresScene.Build(vm.Canvas, routerPlaced: true);
        UiInput.RunJobs();
        var canvasControl = UiInput.Descendants<DesignCanvas>(win).First();
        canvasControl.Zoom = 2.0;
        vm.Canvas.PanX = canvasControl.Bounds.Width / 2 - CrossedWiresScene.Center * canvasControl.Zoom;
        vm.Canvas.PanY = canvasControl.Bounds.Height / 2 - CrossedWiresScene.Center * canvasControl.Zoom;
        Point CanvasPoint(double x, double y) =>
            canvasControl.TranslatePoint(
                new Point(x * canvasControl.Zoom + vm.Canvas.PanX, y * canvasControl.Zoom + vm.Canvas.PanY), win)!.Value;

        double center = CrossedWiresScene.Center;
        win.MouseMove(CanvasPoint(center, center));
        win.MouseDown(CanvasPoint(center, center), MouseButton.Left, RawInputModifiers.LeftMouseButton);
        win.MouseMove(CanvasPoint(center + DragX, center + DragYJitter), RawInputModifiers.LeftMouseButton);
        UiInput.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = win.CaptureRenderedFrame();
        frame.ShouldNotBeNull();
        Directory.CreateDirectory(Path.Combine(shotDir, "crossing-axis-drag"));
        frame!.Save(Path.Combine(shotDir, "crossing-axis-drag", "01-drag-along-horizontal-wire.png"));
        win.MouseUp(CanvasPoint(center + DragX, center + DragYJitter), MouseButton.Left);
        UiInput.RunJobs();
    }

    private static void AssertPatched(WaveguideConnection piece, List<((double X, double Y) Start, (double X, double Y) End)> before,
                                      double endMovedBy = 0, double startMovedBy = 0)
    {
        var after = CrossedWiresScene.Geometry(piece);
        after.Count.ShouldBe(before.Count, "the along-axis piece is patched, not re-routed");
        after[0].Start.X.ShouldBe(before[0].Start.X + startMovedBy, 1e-6);
        after[^1].End.X.ShouldBe(before[^1].End.X + endMovedBy, 1e-6);
        after[^1].End.Y.ShouldBe(before[^1].End.Y, 1e-9);
    }

    /// <summary>Pumps the dispatcher until the re-routed pieces have routes again (the pass runs off-thread).</summary>
    private static void WaitForRoutes(params WaveguideConnection[] pieces)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            UiInput.RunJobs();
            if (pieces.All(p => p.RoutedPath != null))
            {
                Thread.Sleep(50);
                UiInput.RunJobs();
                return;
            }
            Thread.Sleep(20);
        }
        while (clock.Elapsed < RoutingTimeout);
        throw new TimeoutException("The re-routed crossing pieces never got a route.");
    }
}
