using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using Component = CAP_Core.Components.Core.Component;

namespace CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;

/// <summary>
/// Resolves and watches the receiver chiplet of the "Connect two chiplets"
/// tour: the end chiplet of the cross-chiplet facet link — the same chiplet
/// "Align chiplet" would move. Records its start position, reports how far it
/// has drifted, and performs the tour's "do it for me" shift along the facet
/// axis through the same undoable group move a drag-drop records, so Ctrl+Z
/// restores the aligned position exactly.
/// </summary>
public class ConnectChipletsReceiverTracker
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CommandManager _commandManager;

    private ComponentViewModel? _receiver;
    private double _startX;
    private double _startY;

    /// <summary>Creates a tracker over the given canvas; moves are recorded on the given undo stack.</summary>
    public ConnectChipletsReceiverTracker(DesignCanvasViewModel canvas, CommandManager commandManager)
    {
        _canvas = canvas;
        _commandManager = commandManager;
    }

    /// <summary>Raised when the tracked receiver's canvas position changes (drag, undo, align).</summary>
    public event EventHandler? ReceiverPositionChanged;

    /// <summary>The receiver chiplet's view model, or null when no cross-chiplet link exists.</summary>
    public ComponentViewModel? Receiver => EnsureReceiver();

    /// <summary>Displacement of the receiver from its recorded start position in micrometers.</summary>
    public double DisplacementFromStart()
    {
        var receiver = EnsureReceiver();
        if (receiver == null)
            return 0;
        double dx = receiver.X - _startX;
        double dy = receiver.Y - _startY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Shifts the receiver <paramref name="distanceMicrometers"/> along the
    /// facet axis (away from its partner) through the undoable group move.
    /// </summary>
    public void MoveAlongFacetAxis(double distanceMicrometers)
    {
        var link = FindCrossChipletLink();
        var receiver = EnsureReceiver();
        if (link?.StartPin == null || receiver == null)
            return;

        double axisRadians = link.StartPin.GetAbsoluteAngle() * Math.PI / 180.0;
        double deltaX = distanceMicrometers * Math.Cos(axisRadians);
        double deltaY = distanceMicrometers * Math.Sin(axisRadians);

        _canvas.BeginDragComponent(receiver);
        _canvas.MoveComponent(receiver, deltaX, deltaY);
        _commandManager.ExecuteCommand(new GroupMoveCommand(_canvas, new[] { receiver }, deltaX, deltaY));
        _canvas.EndDragComponent(receiver);
    }

    /// <summary>Re-records the current position as the baseline (e.g. when the tour restarts).</summary>
    public void ResetBaseline()
    {
        if (_receiver != null)
            _receiver.PropertyChanged -= OnReceiverPropertyChanged;
        _receiver = null;
        EnsureReceiver();
    }

    private ComponentViewModel? EnsureReceiver()
    {
        if (_receiver != null && _canvas.Components.Contains(_receiver))
            return _receiver;

        if (_receiver != null)
            _receiver.PropertyChanged -= OnReceiverPropertyChanged;
        var chiplet = TopLevelGroupOf(FindCrossChipletLink()?.EndPin?.ParentComponent);
        _receiver = chiplet == null
            ? null
            : _canvas.Components.FirstOrDefault(vm => ReferenceEquals(vm.Component, chiplet));
        if (_receiver != null)
        {
            _startX = _receiver.X;
            _startY = _receiver.Y;
            _receiver.PropertyChanged += OnReceiverPropertyChanged;
        }
        return _receiver;
    }

    private void OnReceiverPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ComponentViewModel.X) or nameof(ComponentViewModel.Y))
            ReceiverPositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private WaveguideConnection? FindCrossChipletLink() =>
        _canvas.Connections.Select(c => c.Connection)
            .FirstOrDefault(c => c.IsCrossChipletFacetLink);

    /// <summary>Walks the parent-group chain to the outermost group (the chiplet).</summary>
    private static ComponentGroup? TopLevelGroupOf(Component? component)
    {
        var group = component?.ParentGroup as ComponentGroup;
        if (group == null)
            return null;
        while (group.ParentGroup is ComponentGroup parent)
        {
            group = parent;
        }
        return group;
    }
}
