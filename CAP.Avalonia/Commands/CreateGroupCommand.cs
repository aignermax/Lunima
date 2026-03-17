using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.Services;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using CAP_Core.LightCalculation;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command to create a ComponentGroup template from selected components.
/// Captures component structure and connections as a reusable template (Unity Prefab pattern).
/// The template is saved to the library and can be instantiated as ungrouped components.
/// Does NOT create live groups on canvas - use PlaceTemplateCommand to place templates.
/// </summary>
public class CreateGroupCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly List<Component> _components;
    private ComponentGroup? _createdTemplate;
    private readonly List<WaveguideConnection> _internalConnections = new();
    private readonly ComponentLibraryViewModel? _libraryViewModel;

    public CreateGroupCommand(
        DesignCanvasViewModel canvas,
        List<ComponentViewModel> components,
        ComponentLibraryViewModel? libraryViewModel = null)
    {
        _canvas = canvas;
        _components = components.Select(c => c.Component).ToList();
        _libraryViewModel = libraryViewModel;
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

        // 2. Identify internal connections (connections between selected components)
        var componentSet = new HashSet<Component>(_components);
        _internalConnections.Clear();

        foreach (var conn in _canvas.ConnectionManager.Connections)
        {
            bool startInGroup = componentSet.Contains(conn.StartPin.ParentComponent);
            bool endInGroup = componentSet.Contains(conn.EndPin.ParentComponent);

            if (startInGroup && endInGroup)
            {
                _internalConnections.Add(conn);
            }
        }

        // 3. Create ComponentGroup template (not placed on canvas)
        _createdTemplate = new ComponentGroup($"Template_{DateTime.Now:HHmmss}")
        {
            PhysicalX = minX,
            PhysicalY = minY,
            Description = $"Template of {_components.Count} components",
            IsPrefab = true // Mark as template
        };

        // 4. Clone components into template (deep copy to avoid canvas reference issues)
        var componentMap = new Dictionary<Component, Component>();

        foreach (var comp in _components)
        {
            // Create a shallow clone
            var cloned = CloneComponent(comp);
            componentMap[comp] = cloned;
            _createdTemplate.AddChild(cloned);
        }

        // 5. Create frozen paths for internal connections
        foreach (var conn in _internalConnections)
        {
            if (conn.RoutedPath != null)
            {
                var startCompClone = componentMap[conn.StartPin.ParentComponent];
                var endCompClone = componentMap[conn.EndPin.ParentComponent];

                var startPinClone = startCompClone.PhysicalPins.First(p => p.Name == conn.StartPin.Name);
                var endPinClone = endCompClone.PhysicalPins.First(p => p.Name == conn.EndPin.Name);

                var frozenPath = new FrozenWaveguidePath
                {
                    Path = ClonePath(conn.RoutedPath),
                    StartPin = startPinClone,
                    EndPin = endPinClone
                };
                _createdTemplate.AddInternalPath(frozenPath);
            }
        }

        // 6. Update template bounds
        _createdTemplate.UpdateGroupBounds();

        // 7. Add template to library if library ViewModel is provided
        if (_libraryViewModel != null)
        {
            var libraryManager = _libraryViewModel.GetLibraryManager();
            var template = libraryManager.SaveTemplate(
                _createdTemplate,
                _createdTemplate.GroupName,
                _createdTemplate.Description,
                "User");
            _libraryViewModel.AddTemplate(template);
        }

        // NOTE: Components remain on canvas unchanged. Template is library-only.
        // User can now drag template from library to instantiate as ungrouped components.
    }

    /// <summary>
    /// Creates a shallow clone of a Component with a new unique identifier.
    /// </summary>
    private Component CloneComponent(Component source)
    {
        var cloned = new Component(
            new Dictionary<int, SMatrix>(source.WaveLengthToSMatrixMap),
            source.GetAllSliders().Select(s => new Slider(
                Guid.NewGuid(),
                s.Number,
                s.Value,
                s.MaxValue,
                s.MinValue)).ToList(),
            source.NazcaFunctionName,
            source.NazcaFunctionParameters,
            source.Parts,
            source.TypeNumber,
            $"{source.Identifier}_{Guid.NewGuid():N}",
            source.Rotation90CounterClock,
            source.PhysicalPins.Select(p => new PhysicalPin
            {
                Name = p.Name,
                OffsetXMicrometers = p.OffsetXMicrometers,
                OffsetYMicrometers = p.OffsetYMicrometers,
                AngleDegrees = p.AngleDegrees,
                LogicalPin = p.LogicalPin
            }).ToList()
        )
        {
            PhysicalX = source.PhysicalX,
            PhysicalY = source.PhysicalY,
            WidthMicrometers = source.WidthMicrometers,
            HeightMicrometers = source.HeightMicrometers,
            NazcaOriginOffsetX = source.NazcaOriginOffsetX,
            NazcaOriginOffsetY = source.NazcaOriginOffsetY,
            NazcaModuleName = source.NazcaModuleName,
            IsLocked = false
        };

        return cloned;
    }

    public void Undo()
    {
        if (_createdTemplate == null)
            return;

        // Remove template from library if library ViewModel is provided
        if (_libraryViewModel != null)
        {
            _libraryViewModel.RemoveTemplateByName(_createdTemplate.GroupName);
        }

        _createdTemplate = null;

        // NOTE: Canvas remains unchanged (components were never removed)
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
