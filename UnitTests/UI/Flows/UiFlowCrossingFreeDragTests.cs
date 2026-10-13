using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Components.Connections;
using Shouldly;
using UnitTests.Routing.CrossingInsertion;
using UnitTests.Routing.CrossingInsertion.CrossingMove;
using Xunit;

namespace UnitTests.UI.Flows;

/// <summary>
/// User stories for snapping and freeing a crossing drag: an axis drag snaps the crossing so
/// a port lines up with a nearby facing pin (with the usual guide shown), and Shift — held at
/// the press or pressed mid-drag — turns the drag into a free move whose docked wires are
/// re-routed. Every move is one undo step.
/// </summary>
[Trait("Category", "UiFlows")]
// Boots the real MainWindow through the input pipeline — CI-only (local runs exclude Slow).
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class UiFlowCrossingFreeDragTests
{
    private const double DragX = 40;
    private const double DragYJitter = 6;
    private const double SnapGap = 3;
    private const double SnapPinY = 150;
    /// <summary>A free component move lands on the 0.1 µm placement quantum (the scene's crossing starts off it).</summary>
    private const double PlacementQuantumMicrometers = 0.1;
    private static readonly TimeSpan RoutingTimeout = TimeSpan.FromSeconds(30);

    [AvaloniaFact]
    public void AxisDrag_NearAFacingPin_SnapsThePortOntoItsX_ShowsTheGuide_AndUndoes()
    {
        using var flow = new Flow(routerPlaced: true);
        var scene = flow.Scene;
        double snapX = CrossedWiresScene.Center + DragX + SnapGap;
        AddDownFacingPinAt(flow, snapX, SnapPinY);
        double startX = scene.Crossing.PhysicalX, startY = scene.Crossing.PhysicalY;
        double c = CrossedWiresScene.Center;

        flow.Win.MouseMove(flow.At(c, c));
        flow.Win.MouseDown(flow.At(c, c), MouseButton.Left, RawInputModifiers.LeftMouseButton);
        flow.Win.MouseMove(flow.At(c + DragX, c + DragYJitter), RawInputModifiers.LeftMouseButton);
        UiInput.RunJobs();
        var guides = flow.Vm.Canvas.AlignmentGuide.VerticalAlignments;
        guides.ShouldNotBeEmpty("the snap shows the usual pin alignment guide");
        guides.ShouldAllBe(g => Math.Abs(g.XCoordinate - snapX) < 1e-6);
        flow.Vm.CanvasInteraction.ModeShortcutHints.ShouldBe(
            LocalizationService.Instance.Translate("StatusBar.Hints.CrossingDrag"), "the Shift modifier is advertised while dragging");
        flow.Win.MouseUp(flow.At(c + DragX, c + DragYJitter), MouseButton.Left);
        WaitUntil(() => scene.Pieces.All(p => p.RoutedPath != null && EndsOnPins(p)));

        scene.Crossing.PhysicalY.ShouldBe(startY, 1e-9, "an axis drag stays on the horizontal wire");
        (scene.Crossing.PhysicalX - startX).ShouldBe(DragX + SnapGap, 1e-6, "the north port snapped onto the facing pin's X");
        flow.Vm.Canvas.AlignmentGuide.HasAlignments.ShouldBeFalse("guides vanish on release");
        flow.Vm.CanvasInteraction.ModeShortcutHints.ShouldNotBe(
            LocalizationService.Instance.Translate("StatusBar.Hints.CrossingDrag"));

        flow.Vm.CommandManager.Undo().ShouldBeTrue();
        WaitUntil(() => scene.Pieces.All(EndsOnPins));
        scene.Crossing.PhysicalX.ShouldBe(startX, 1e-9);
    }

    [AvaloniaFact]
    public void ShiftPressedMidDrag_FreesTheDrag_MovesDiagonally_ReroutesTheWires_AndUndoes()
    {
        using var flow = new Flow(routerPlaced: true);
        var scene = flow.Scene;
        double startX = scene.Crossing.PhysicalX, startY = scene.Crossing.PhysicalY;
        double c = CrossedWiresScene.Center;

        flow.Win.MouseMove(flow.At(c, c));
        flow.Win.MouseDown(flow.At(c, c), MouseButton.Left, RawInputModifiers.LeftMouseButton);
        flow.Win.MouseMove(flow.At(c + DragX / 2, c), RawInputModifiers.LeftMouseButton);
        UiInput.RunJobs();
        var shift = RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift;
        flow.Win.MouseMove(flow.At(c + DragX / 2, c + DragX / 2), shift);
        flow.Win.MouseMove(flow.At(c + DragX, c + DragX), shift);
        flow.Win.MouseUp(flow.At(c + DragX, c + DragX), MouseButton.Left, RawInputModifiers.Shift);

        AssertMovedFreely(scene, startX, startY);
        flow.Vm.CommandManager.Undo().ShouldBeTrue();
        WaitUntil(() => scene.Pieces.All(EndsOnPins));
        scene.Crossing.PhysicalX.ShouldBe(startX, PlacementQuantumMicrometers);
        scene.Crossing.PhysicalY.ShouldBe(startY, PlacementQuantumMicrometers);
    }

    [AvaloniaFact]
    public void ShiftHeldAtPress_MovesARouterPlacedCrossingFreely_AndUndoes()
    {
        using var flow = new Flow(routerPlaced: true);
        var scene = flow.Scene;
        double startX = scene.Crossing.PhysicalX, startY = scene.Crossing.PhysicalY;
        double c = CrossedWiresScene.Center;

        flow.Win.MouseMove(flow.At(c, c), RawInputModifiers.Shift);
        flow.Win.MouseDown(flow.At(c, c), MouseButton.Left, RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
        flow.Win.MouseMove(flow.At(c + DragX, c + DragX), RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
        flow.Win.MouseUp(flow.At(c + DragX, c + DragX), MouseButton.Left, RawInputModifiers.Shift);

        AssertMovedFreely(scene, startX, startY);
        flow.Vm.CommandManager.Undo().ShouldBeTrue();
        scene.Crossing.PhysicalX.ShouldBe(startX, PlacementQuantumMicrometers);
        scene.Crossing.PhysicalY.ShouldBe(startY, PlacementQuantumMicrometers);
    }

    /// <summary>Opt-in (UI_SHOT_DIR): the axis drag snapped onto a facing pin, with the pin guide and the Shift hint.</summary>
    [AvaloniaFact]
    public void CaptureSnappedAxisDrag()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR"))) return;
        using var flow = new Flow(routerPlaced: true);
        double c = CrossedWiresScene.Center;
        AddDownFacingPinAt(flow, c + DragX + SnapGap, SnapPinY);
        flow.ZoomOnto(c, (c + SnapPinY) / 2, zoom: 1.6);

        flow.Win.MouseMove(flow.At(c, c));
        flow.Win.MouseDown(flow.At(c, c), MouseButton.Left, RawInputModifiers.LeftMouseButton);
        flow.Win.MouseMove(flow.At(c + DragX, c + DragYJitter), RawInputModifiers.LeftMouseButton);
        UiInput.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = flow.Win.CaptureRenderedFrame();
        frame.ShouldNotBeNull();
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("crossing-drag-snap");
        Directory.CreateDirectory(dir);
        frame!.Save(Path.Combine(dir, "01-axis-drag-snaps-to-pin.png"));
        flow.Win.MouseUp(flow.At(c + DragX, c + DragYJitter), MouseButton.Left);
        UiInput.RunJobs();
    }

    private static void AssertMovedFreely(CrossedWiresScene scene, double startX, double startY)
    {
        WaitUntil(() => scene.Pieces.All(p => p.IsPathValid && EndsOnPins(p)));
        (scene.Crossing.PhysicalX - startX).ShouldBe(DragX, DragX / 4, "the crossing follows the pointer in X");
        (scene.Crossing.PhysicalY - startY).ShouldBe(DragX, DragX / 4, "Shift frees the drag from the wire axes");
        foreach (var piece in scene.Pieces)
            piece.IsPathValid.ShouldBeTrue("every docked piece is re-routed to the moved crossing");
    }

    /// <summary>A terminal whose pin points down (south) at (<paramref name="pinX"/>, <paramref name="pinY"/>), facing the crossing's north port.</summary>
    private static void AddDownFacingPinAt(Flow flow, double pinX, double pinY)
    {
        const double SouthDegrees = 90;
        var terminal = CrossingTestCircuit.CreateTerminal("snap target", pinX, pinY, SouthDegrees);
        var (x, y) = terminal.PhysicalPin.GetAbsolutePosition();
        terminal.Component.PhysicalX += pinX - x;
        terminal.Component.PhysicalY += pinY - y;
        flow.Vm.Canvas.AddComponent(terminal.Component, "Terminal");
        UiInput.RunJobs();
    }

    private static bool EndsOnPins(WaveguideConnection piece)
    {
        var segments = piece.GetPathSegments().ToList();
        if (segments.Count == 0) return false;
        var start = piece.StartPin.GetAbsolutePosition();
        var end = piece.EndPin.GetAbsolutePosition();
        return Near(segments[0].StartPoint, start) && Near(segments[^1].EndPoint, end);
    }

    private static bool Near((double X, double Y) a, (double X, double Y) b) =>
        Math.Abs(a.X - b.X) < 1e-3 && Math.Abs(a.Y - b.Y) < 1e-3;

    /// <summary>Pumps the dispatcher until <paramref name="condition"/> holds (re-routes run off-thread).</summary>
    private static void WaitUntil(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            UiInput.RunJobs();
            if (condition())
                return;
            Thread.Sleep(20);
        }
        while (clock.Elapsed < RoutingTimeout);
        throw new TimeoutException("The crossing's pieces never settled on their pins.");
    }

    /// <summary>The booted main window with the crossed-wires scene and a canvas→window point mapper.</summary>
    private sealed class Flow : IDisposable
    {
        private readonly UiFlowTestHost _host = new();
        private readonly DesignCanvas _canvas;

        public Flow(bool routerPlaced)
        {
            Scene = CrossedWiresScene.Build(_host.Vm.Canvas, routerPlaced);
            UiInput.RunJobs();
            _canvas = UiInput.Descendants<DesignCanvas>(_host.Window).First();
        }

        public CrossedWiresScene Scene { get; }
        public CAP.Avalonia.Views.MainWindow Win => _host.Window;
        public CAP.Avalonia.ViewModels.MainViewModel Vm => _host.Vm;

        public void ZoomOnto(double x, double y, double zoom)
        {
            _canvas.Zoom = zoom;
            Vm.Canvas.PanX = _canvas.Bounds.Width / 2 - x * zoom;
            Vm.Canvas.PanY = _canvas.Bounds.Height / 2 - y * zoom;
            UiInput.RunJobs();
        }

        public Point At(double x, double y) =>
            _canvas.TranslatePoint(new Point(x * _canvas.Zoom + Vm.Canvas.PanX, y * _canvas.Zoom + Vm.Canvas.PanY), Win)!.Value;

        public void Dispose() => _host.Dispose();
    }
}
