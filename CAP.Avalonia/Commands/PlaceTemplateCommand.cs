using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Routing;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command to place a ComponentGroup template on the canvas as ungrouped components.
/// Implements the Unity Prefab pattern: templates are instantiated as individual components,
/// not as live groups. This avoids edit mode complexity and keeps the canvas flat.
/// </summary>
public class PlaceTemplateCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentGroup _template;
    private readonly double _placeX;
    private readonly double _placeY;
    private readonly List<Component> _placedComponents = new();
    private readonly List<ComponentViewModel> _placedViewModels = new();

    /// <summary>
    /// Creates a command to place a template (ComponentGroup) as ungrouped components.
    /// </summary>
    /// <param name="canvas">Target canvas</param>
    /// <param name="template">Template to instantiate</param>
    /// <param name="placeX">X position in micrometers</param>
    /// <param name="placeY">Y position in micrometers</param>
    public PlaceTemplateCommand(
        DesignCanvasViewModel canvas,
        ComponentGroup template,
        double placeX,
        double placeY)
    {
        _canvas = canvas;
        _template = template;
        _placeX = placeX;
        _placeY = placeY;
    }

    public string Description => $"Place template '{_template.GroupName}'";

    public void Execute()
    {
        try
        {
            _canvas.BeginCommandExecution();

            // Deep copy the template to get new component instances
            var templateCopy = _template.DeepCopy();

            // Calculate offset from template origin to placement position
            double offsetX = _placeX - templateCopy.PhysicalX;
            double offsetY = _placeY - templateCopy.PhysicalY;

            // Generate unique instance ID for this template placement
            var instanceId = Guid.NewGuid();

            // Extract all child components and place them at the target location
            foreach (var child in templateCopy.ChildComponents)
            {
                // Apply offset to position child at final location
                child.PhysicalX += offsetX;
                child.PhysicalY += offsetY;

                // Clear parent group reference (components are now top-level)
                child.ParentGroup = null;

                // Mark component with source template for reference
                child.SourceTemplate = _template.GroupName;

                // Assign shared instance ID to group components from same placement
                child.TemplateInstanceId = instanceId;

                // Add to canvas
                var childVm = _canvas.AddComponent(child);
                _placedComponents.Add(child);
                _placedViewModels.Add(childVm);
            }

            // Create waveguide connections from template's internal paths
            foreach (var frozenPath in templateCopy.InternalPaths)
            {
                // Find the cloned pins in the placed components
                var startComp = _placedComponents.First(c =>
                    c.Identifier == frozenPath.StartPin.ParentComponent.Identifier);
                var endComp = _placedComponents.First(c =>
                    c.Identifier == frozenPath.EndPin.ParentComponent.Identifier);

                var startPin = startComp.PhysicalPins.First(p =>
                    p.Name == frozenPath.StartPin.Name);
                var endPin = endComp.PhysicalPins.First(p =>
                    p.Name == frozenPath.EndPin.Name);

                // Add connection (will be auto-routed by RecalculateRoutesAsync)
                _canvas.ConnectionManager.AddConnection(startPin, endPin);
            }

            // Select all placed components for visual feedback
            _canvas.Selection.ClearSelection();
            foreach (var vm in _placedViewModels)
            {
                _canvas.Selection.AddToSelection(vm);
            }

            // Recalculate waveguide routes (async)
            _ = _canvas.RecalculateRoutesAsync();
            _canvas.InvalidateSimulation();
        }
        finally
        {
            _canvas.EndCommandExecution();
        }
    }

    public void Undo()
    {
        try
        {
            _canvas.BeginCommandExecution();

            // Remove all placed components and their connections
            foreach (var compVm in _placedViewModels)
            {
                _canvas.RemoveComponent(compVm);
            }

            _placedComponents.Clear();
            _placedViewModels.Clear();
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
