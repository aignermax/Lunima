using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.Gestures;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Logic.GateView.Layout;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Core;
using CAP_Core.Components.PinKinds;

namespace CAP.Avalonia.Controls.Canvas.GateView;

/// <summary>Connects schematic terminals through the normal physical, undoable wire command.</summary>
internal sealed class GateConnectionGestureRecognizer(
    Func<GateViewScene> scene, Func<double> zoom, Action invalidate) : IGestureRecognizer
{
    private GateTerminal? _start;
    private GateTerminal? _hover;
    private Point _point;

    private static bool Enabled(MainViewModel? vm) =>
        vm?.GateView.IsShown == true && vm.CanvasInteraction.CurrentMode == InteractionMode.Connect;

    public bool TryRecognize(PointerPressedEventArgs e, Point point, DesignCanvasViewModel canvas, MainViewModel? vm)
    {
        if (!Enabled(vm) || !e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return false;
        _start = GateTerminalHitTesting.HitTest(scene(), point, zoom());
        _point = point;
        canvas.ClearPinHighlight();
        if (_start == null)
        {
            vm!.CanvasInteraction.CurrentMode = InteractionMode.Select;
            return false;
        }
        invalidate();
        return true;
    }

    public void UpdatePassiveState(Point point, DesignCanvasViewModel canvas, MainViewModel? vm)
    {
        if (!Enabled(vm))
        {
            if (_start != null || _hover != null) Cancel();
            return;
        }
        _point = point;
        _hover = GateTerminalHitTesting.HitTest(scene(), point, zoom());
        canvas.ClearPinHighlight();
        invalidate();
    }

    public void OnPointerMoved(PointerEventArgs e, Point delta, Point point, DesignCanvasViewModel canvas, MainViewModel? vm)
    {
        _point = point;
        if (_start != null && _hover != null && Refusal(_start, _hover) is { } refusal && vm != null)
            vm.StatusText = refusal;
        invalidate();
    }

    public void OnPointerReleased(PointerReleasedEventArgs e, DesignCanvasViewModel canvas, MainViewModel? vm)
    {
        var start = _start;
        var target = GateTerminalHitTesting.HitTest(scene(), _point, zoom());
        Cancel();
        if (!Enabled(vm) || start == null || target == null || start.Pin == target.Pin) return;
        if (!IsPresent(canvas, start.Pin) || !IsPresent(canvas, target.Pin)) return;
        if (GateTerminalHitTesting.HitTest(scene(), start.Position, zoom())?.Pin != start.Pin) return;
        if (Refusal(start, target) is { } refusal)
        {
            vm!.StatusText = refusal;
            return;
        }
        var source = start.IsOutput ? start.Pin : target.Pin;
        var sink = start.IsOutput ? target.Pin : start.Pin;
        var command = new CreateConnectionCommand(canvas, source, sink);
        if (command.CrossesDieEdge)
        {
            vm!.StatusText = LocalizationService.Instance.Translate("Dies.Wiring.CouplersOnly");
            return;
        }
        vm!.CommandManager.ExecuteCommand(command);
    }

    private static bool IsPresent(DesignCanvasViewModel canvas, PhysicalPin pin)
    {
        for (Component? part = pin.ParentComponent; part != null; part = part.ParentGroup as Component)
            if (canvas.Components.Any(vm => ReferenceEquals(vm.Component, part))) return true;
        return false;
    }

    private static string? Refusal(GateTerminal start, GateTerminal target)
    {
        if (start.IsOutput == target.IsOutput)
            return LocalizationService.Instance.Translate("GateView.ConnectDirection");
        if (!PinKindHelper.AreKindsCompatible(start.Pin, target.Pin))
            return PinKindHelper.DescribeIncompatibility(start.Pin, target.Pin);
        return PolarizationRules.CanConnect(start.Pin.Polarization, target.Pin.Polarization)
            ? null : PolarizationRules.GetMismatchMessage(start.Pin, target.Pin);
    }

    public void Cancel()
    {
        _start = null;
        _hover = null;
        invalidate();
    }

    internal void Render(DrawingContext context, MainViewModel? vm)
    {
        if (!Enabled(vm)) return;
        var scale = 1 / Math.Max(zoom(), 0.001);
        var valid = _start != null && _hover != null && _start.Pin != _hover.Pin && Refusal(_start, _hover) == null;
        var pen = new Pen(valid ? CanvasPalette.AccentHoverBrush : CanvasPalette.TextTertiaryBrush, 2 * scale);
        if (_start != null)
        {
            context.DrawLine(pen, _start.Position, _hover?.Position ?? _point);
            context.DrawEllipse(null, pen, _start.Position, 5 * scale, 5 * scale);
        }
        if (_hover != null)
            context.DrawEllipse(null, pen, _hover.Position, 5 * scale, 5 * scale);
    }
}
