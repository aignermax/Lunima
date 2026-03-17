using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command to create a ComponentGroup from selected components.
/// Creates a simple bounding box container - internal waveguides remain as live WaveguideConnections.
/// Does NOT automatically save to library - use SaveGroupAsPrefabCommand for that.
/// </summary>
public class CreateGroupCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly List<Component> _components;
    private ComponentGroup? _createdGroup;
    private ComponentViewModel? _groupViewModel;
    private readonly Dictionary<Component, (double x, double y)> _originalPositions = new();

    public CreateGroupCommand(
        DesignCanvasViewModel canvas,
        List<ComponentViewModel> components)
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

        // 2. Create ComponentGroup
        _createdGroup = new ComponentGroup($"Group_{DateTime.Now:HHmmss}")
        {
            PhysicalX = minX,
            PhysicalY = minY,
            Description = $"Group of {_components.Count} components"
        };

        // 3. Add child components to group
        foreach (var comp in _components)
        {
            _createdGroup.AddChild(comp);
        }

        try
        {
            _canvas.BeginCommandExecution();

            // 4. Remove individual components from canvas
            var componentsToRemove = _canvas.Components
                .Where(cvm => _components.Contains(cvm.Component))
                .ToList();

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

            // 5. Add group to canvas
            _groupViewModel = _canvas.AddComponent(_createdGroup);

            // 6. Select the newly created group so user gets visual feedback
            _canvas.Selection.SelectSingle(_groupViewModel);
            _canvas.SelectedComponent = _groupViewModel;
        }
        finally
        {
            _canvas.EndCommandExecution();
        }

        // Recalculate routes for ALL waveguides (internal and external)
        _ = _canvas.RecalculateRoutesAsync();
        _canvas.InvalidateSimulation();

        // NOTE: Groups are NOT auto-saved to library anymore.
        // User must explicitly use "Save as Prefab" action.
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
