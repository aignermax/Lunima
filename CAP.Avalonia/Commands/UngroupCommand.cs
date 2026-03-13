using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command to explode a ComponentGroup back to individual components.
/// Unfreezes internal paths and restores waveguide connections.
/// </summary>
public class UngroupCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentGroup _group;
    private ComponentViewModel? _groupViewModel;
    private readonly List<ComponentViewModel> _restoredComponentViewModels = new();
    private readonly List<WaveguideConnection> _restoredConnections = new();
    private readonly double _groupX;
    private readonly double _groupY;

    public UngroupCommand(DesignCanvasViewModel canvas, ComponentGroup group)
    {
        _canvas = canvas;
        _group = group;
        _groupX = group.PhysicalX;
        _groupY = group.PhysicalY;
    }

    public string Description => $"Ungroup {_group.GroupName}";

    public void Execute()
    {
        // Find the group's ViewModel
        _groupViewModel = _canvas.Components.FirstOrDefault(c => c.Component == _group);
        if (_groupViewModel == null)
            return;

        try
        {
            _canvas.BeginCommandExecution();

            // 1. Extract child components from group
            var children = _group.ChildComponents.ToList();
            _restoredComponentViewModels.Clear();

            foreach (var child in children)
            {
                child.ParentGroup = null;
                var childVm = _canvas.AddComponent(child);
                _restoredComponentViewModels.Add(childVm);
            }

            // 2. Convert frozen paths back to WaveguideConnections
            // IMPORTANT: Do NOT use cached routes - they're from before group movement!
            // The group may have been moved, rotated, or edited since grouping.
            // We must recalculate routes to reflect current component positions.
            _restoredConnections.Clear();
            foreach (var frozenPath in _group.InternalPaths)
            {
                // Add connection without route - will be recalculated below
                var connection = _canvas.ConnectionManager.AddConnectionDeferred(
                    frozenPath.StartPin,
                    frozenPath.EndPin
                );

                var connVm = new WaveguideConnectionViewModel(connection);
                _canvas.Connections.Add(connVm);
                _restoredConnections.Add(connection);
            }

            // 3. Remove the group from canvas
            _canvas.RemoveComponent(_groupViewModel);
        }
        finally
        {
            _canvas.EndCommandExecution();
        }

        // Update routes for the restored connections
        _ = _canvas.RecalculateRoutesAsync();
        _canvas.InvalidateSimulation();
    }

    public void Undo()
    {
        if (_groupViewModel == null)
            return;

        try
        {
            _canvas.BeginCommandExecution();

            // Remove restored connections
            foreach (var conn in _restoredConnections)
            {
                var connVm = _canvas.Connections.FirstOrDefault(c => c.Connection == conn);
                if (connVm != null)
                {
                    _canvas.Connections.Remove(connVm);
                    _canvas.ConnectionManager.RemoveConnectionDeferred(conn);
                }
            }

            // Remove individual components
            foreach (var compVm in _restoredComponentViewModels)
            {
                // Set component back as child of group
                compVm.Component.ParentGroup = _group;

                // Remove pins
                var pinsToRemove = _canvas.AllPins
                    .Where(p => p.ParentComponentViewModel == compVm)
                    .ToList();
                foreach (var pin in pinsToRemove)
                {
                    _canvas.AllPins.Remove(pin);
                }

                _canvas.Components.Remove(compVm);
            }

            // Re-add the group
            _group.PhysicalX = _groupX;
            _group.PhysicalY = _groupY;
            _groupViewModel = _canvas.AddComponent(_group);
        }
        finally
        {
            _canvas.EndCommandExecution();
        }

        // Recalculate routes
        _ = _canvas.RecalculateRoutesAsync();
        _canvas.InvalidateSimulation();
    }
}
