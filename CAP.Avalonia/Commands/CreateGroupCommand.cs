using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command to create a ComponentGroup from selected components.
/// Captures current positions and waveguide paths as frozen geometry.
/// </summary>
public class CreateGroupCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly List<Component> _components;
    private ComponentGroup? _createdGroup;
    private ComponentViewModel? _groupViewModel;
    private readonly List<WaveguideConnection> _internalConnections = new();
    private readonly List<WaveguideConnection> _externalConnections = new();
    private readonly List<WaveguideConnectionViewModel> _internalConnectionViewModels = new();
    private readonly Dictionary<Component, (double x, double y)> _originalPositions = new();

    public CreateGroupCommand(DesignCanvasViewModel canvas, List<ComponentViewModel> components)
    {
        _canvas = canvas;
        _components = components.Select(c => c.Component).ToList();

        // Store original positions
        foreach (var comp in _components)
        {
            _originalPositions[comp] = (comp.PhysicalX, comp.PhysicalY);
        }
    }

    public string Description => $"Create group from {_components.Count} components";

    public void Execute()
    {
        if (_components.Count < 2)
            return;

        // Don't group locked components
        if (_components.Any(c => c.IsLocked))
            return;

        // 1. Calculate bounding box for selected components
        double minX = _components.Min(c => c.PhysicalX);
        double minY = _components.Min(c => c.PhysicalY);
        double maxX = _components.Max(c => c.PhysicalX + c.WidthMicrometers);
        double maxY = _components.Max(c => c.PhysicalY + c.HeightMicrometers);

        // 2. Identify internal vs external waveguide connections
        var componentSet = new HashSet<Component>(_components);
        _internalConnections.Clear();
        _externalConnections.Clear();

        foreach (var conn in _canvas.ConnectionManager.Connections)
        {
            bool startInGroup = componentSet.Contains(conn.StartPin.ParentComponent);
            bool endInGroup = componentSet.Contains(conn.EndPin.ParentComponent);

            if (startInGroup && endInGroup)
            {
                _internalConnections.Add(conn);
            }
            else if (startInGroup || endInGroup)
            {
                _externalConnections.Add(conn);
            }
        }

        // 3. Create ComponentGroup
        _createdGroup = new ComponentGroup($"Group_{DateTime.Now:HHmmss}")
        {
            PhysicalX = minX,
            PhysicalY = minY,
            Description = $"Group of {_components.Count} components"
        };

        // 4. Add child components to group
        foreach (var comp in _components)
        {
            _createdGroup.AddChild(comp);
        }

        // 5. Create frozen paths for internal connections
        foreach (var conn in _internalConnections)
        {
            if (conn.RoutedPath != null)
            {
                var frozenPath = new FrozenWaveguidePath
                {
                    Path = ClonePath(conn.RoutedPath),
                    StartPin = conn.StartPin,
                    EndPin = conn.EndPin
                };
                _createdGroup.AddInternalPath(frozenPath);
            }
        }

        // 6. Create GroupPins for external connections
        foreach (var conn in _externalConnections)
        {
            PhysicalPin internalPin;
            if (componentSet.Contains(conn.StartPin.ParentComponent))
                internalPin = conn.StartPin;
            else
                internalPin = conn.EndPin;

            var (pinX, pinY) = internalPin.GetAbsolutePosition();
            var groupPin = new GroupPin
            {
                Name = $"{internalPin.ParentComponent.Identifier}_{internalPin.Name}",
                InternalPin = internalPin,
                RelativeX = pinX - _createdGroup.PhysicalX,
                RelativeY = pinY - _createdGroup.PhysicalY,
                AngleDegrees = internalPin.GetAbsoluteAngle()
            };
            _createdGroup.AddExternalPin(groupPin);
        }

        try
        {
            _canvas.BeginCommandExecution();

            // 7. Remove individual components from canvas
            var componentsToRemove = _canvas.Components
                .Where(cvm => _components.Contains(cvm.Component))
                .ToList();

            // Store internal connection ViewModels before removing
            _internalConnectionViewModels.Clear();
            foreach (var conn in _internalConnections)
            {
                var connVm = _canvas.Connections.FirstOrDefault(c => c.Connection == conn);
                if (connVm != null)
                {
                    _internalConnectionViewModels.Add(connVm);
                }
            }

            // Remove internal connections from canvas (they're now frozen in the group)
            foreach (var conn in _internalConnections)
            {
                var connVm = _canvas.Connections.FirstOrDefault(c => c.Connection == conn);
                if (connVm != null)
                {
                    _canvas.Connections.Remove(connVm);
                    _canvas.ConnectionManager.RemoveConnectionDeferred(conn);
                }
            }

            foreach (var compVm in componentsToRemove)
            {
                // Remove pins from AllPins
                var pinsToRemove = _canvas.AllPins
                    .Where(p => p.ParentComponentViewModel == compVm)
                    .ToList();
                foreach (var pin in pinsToRemove)
                {
                    _canvas.AllPins.Remove(pin);
                }

                // Remove from Components collection
                _canvas.Components.Remove(compVm);
            }

            // 8. Add group to canvas
            _groupViewModel = _canvas.AddComponent(_createdGroup);
        }
        finally
        {
            _canvas.EndCommandExecution();
        }

        // Recalculate routes for external connections
        _ = _canvas.RecalculateRoutesAsync();
        _canvas.InvalidateSimulation();
    }

    public void Undo()
    {
        if (_createdGroup == null || _groupViewModel == null)
            return;

        try
        {
            _canvas.BeginCommandExecution();

            // Remove the group
            _canvas.RemoveComponent(_groupViewModel);

            // Restore individual components at their original positions
            foreach (var comp in _components)
            {
                if (_originalPositions.TryGetValue(comp, out var pos))
                {
                    comp.PhysicalX = pos.x;
                    comp.PhysicalY = pos.y;
                }
                comp.ParentGroup = null;
                _canvas.AddComponent(comp);
            }

            // Restore internal connections
            foreach (var conn in _internalConnections)
            {
                _canvas.ConnectionManager.AddExistingConnection(conn);
                var connVm = new WaveguideConnectionViewModel(conn);
                _canvas.Connections.Add(connVm);
            }
        }
        finally
        {
            _canvas.EndCommandExecution();
        }

        // Recalculate routes
        _ = _canvas.RecalculateRoutesAsync();
        _canvas.InvalidateSimulation();
    }

    /// <summary>
    /// Creates a deep clone of a RoutedPath.
    /// </summary>
    private RoutedPath ClonePath(RoutedPath source)
    {
        var cloned = new RoutedPath
        {
            IsBlockedFallback = source.IsBlockedFallback,
            IsInvalidGeometry = source.IsInvalidGeometry
        };

        foreach (var segment in source.Segments)
        {
            if (segment is BendSegment bend)
            {
                cloned.Segments.Add(new BendSegment(
                    bend.Center.X,
                    bend.Center.Y,
                    bend.RadiusMicrometers,
                    bend.StartAngleDegrees,
                    bend.SweepAngleDegrees
                ));
            }
            else if (segment is StraightSegment straight)
            {
                cloned.Segments.Add(new StraightSegment(
                    straight.StartPoint.X,
                    straight.StartPoint.Y,
                    straight.EndPoint.X,
                    straight.EndPoint.Y,
                    straight.StartAngleDegrees
                ));
            }
        }

        return cloned;
    }
}
