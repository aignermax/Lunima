using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Routing;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command to place a ComponentGroup template on the canvas as ungrouped components.
/// Implements the Unity Prefab pattern: templates are instantiated as individual components,
/// not as live groups. This avoids edit mode complexity and keeps the canvas flat.
///
/// Workflow:
/// 1. Deep copy template (new GUIDs for all components and paths)
/// 2. Offset child components to placement position
/// 3. Clear ParentGroup references (ungroup components)
/// 4. Add metadata (SourceTemplate, TemplateInstanceId) for tracking
/// 5. Add components to canvas as top-level elements
/// 6. Convert FrozenWaveguideConnection to regular WaveguideConnections
/// 7. Trigger auto-routing for connections
///
/// Result: Ungrouped, top-level components on canvas with waveguide connections.
/// No ComponentGroup instance exists on canvas after this command.
///
/// See docs/ComponentGroup-Architecture.md for design details.
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
    /// <param name="canvas">Target canvas for component placement</param>
    /// <param name="template">Template to instantiate (must have IsPrefab = true)</param>
    /// <param name="placeX">X position in micrometers (template origin)</param>
    /// <param name="placeY">Y position in micrometers (template origin)</param>
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

            // Step 1: Deep copy the template to get new component instances with unique GUIDs
            var templateCopy = _template.DeepCopy();

            // Step 2: Calculate offset from template origin to placement position
            double offsetX = _placeX - templateCopy.PhysicalX;
            double offsetY = _placeY - templateCopy.PhysicalY;

            // Step 3: Generate unique instance ID for this template placement
            // (allows tracking which components came from the same template instance)
            var instanceId = Guid.NewGuid();

            // Step 4: Extract all child components and place them as top-level components
            foreach (var child in templateCopy.ChildComponents)
            {
                // Apply offset to position child at final location
                child.PhysicalX += offsetX;
                child.PhysicalY += offsetY;

                // CRITICAL: Clear parent group reference to ungroup components
                // After this, components are independent top-level elements on canvas
                child.ParentGroup = null;

                // Optional metadata: Mark component with source template for traceability
                child.SourceTemplate = _template.GroupName;

                // Optional metadata: Assign shared instance ID to components from same placement
                child.TemplateInstanceId = instanceId;

                // Add to canvas as top-level component
                var childVm = _canvas.AddComponent(child);
                _placedComponents.Add(child);
                _placedViewModels.Add(childVm);
            }

            // Step 5: Convert template's frozen waveguide paths to live canvas connections
            foreach (var frozenPath in templateCopy.InternalPaths)
            {
                // Find the cloned pins in the placed components
                // (FrozenWaveguidePath stores pin references from the template; we need to
                // find corresponding pins in the newly placed component instances)
                var startComp = _placedComponents.First(c =>
                    c.Identifier == frozenPath.StartPin.ParentComponent.Identifier);
                var endComp = _placedComponents.First(c =>
                    c.Identifier == frozenPath.EndPin.ParentComponent.Identifier);

                var startPin = startComp.PhysicalPins.First(p =>
                    p.Name == frozenPath.StartPin.Name);
                var endPin = endComp.PhysicalPins.First(p =>
                    p.Name == frozenPath.EndPin.Name);

                // Add connection to canvas connection manager
                // (will be auto-routed by RecalculateRoutesAsync, not using frozen geometry)
                _canvas.ConnectionManager.AddConnection(startPin, endPin);
            }

            // Step 6: Select all placed components for visual feedback
            _canvas.Selection.ClearSelection();
            foreach (var vm in _placedViewModels)
            {
                _canvas.Selection.AddToSelection(vm);
            }

            // Step 7: Trigger waveguide auto-routing and simulation update
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
