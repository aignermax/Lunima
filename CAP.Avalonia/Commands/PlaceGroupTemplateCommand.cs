using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for placing a group template instance on the canvas.
/// Creates a deep copy of the template group with new unique identifiers.
/// Returns null from TryCreate if no valid placement position exists.
/// </summary>
public class PlaceGroupTemplateCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly GroupLibraryManager _libraryManager;
    private readonly GroupTemplate _template;
    private readonly double _x;
    private readonly double _y;
    private readonly bool _isValid;
    private readonly ComponentGroup? _groupToPlace;
    private List<ComponentViewModel>? _placedComponentViewModels;

    private PlaceGroupTemplateCommand(
        DesignCanvasViewModel canvas,
        GroupLibraryManager libraryManager,
        GroupTemplate template,
        double x,
        double y,
        bool isValid)
    {
        _canvas = canvas;
        _libraryManager = libraryManager;
        _template = template;
        _x = x;
        _y = y;
        _isValid = isValid;

        // Create the group instance in the constructor so Execute/Undo/Execute reuses the same instance
        if (isValid && template.TemplateGroup != null)
        {
            _groupToPlace = _libraryManager.InstantiateTemplate(template, x, y);
            _groupToPlace.IsPrefab = false; // Instance, not prefab
        }
    }

    /// <summary>
    /// Tries to create a placement command. Returns null if no valid position exists.
    /// </summary>
    public static PlaceGroupTemplateCommand? TryCreate(
        DesignCanvasViewModel canvas,
        GroupLibraryManager libraryManager,
        GroupTemplate template,
        double x,
        double y)
    {
        if (template.TemplateGroup == null)
        {
            return null; // Template not loaded
        }

        // Center the group at the click position
        double centeredX = x - template.WidthMicrometers / 2;
        double centeredY = y - template.HeightMicrometers / 2;

        var validPosition = canvas.FindValidPlacement(
            centeredX,
            centeredY,
            template.WidthMicrometers,
            template.HeightMicrometers);

        if (validPosition == null)
        {
            return null; // No space available
        }

        return new PlaceGroupTemplateCommand(
            canvas,
            libraryManager,
            template,
            validPosition.Value.x,
            validPosition.Value.y,
            true);
    }

    public string Description => $"Place group template '{_template.Name}'";

    public void Execute()
    {
        if (!_isValid || _groupToPlace == null)
            return;

        // Add the group as a single component to the canvas
        // The DesignCanvasViewModel will handle group components appropriately
        var groupVm = _canvas.AddComponent(_groupToPlace);
        _placedComponentViewModels = new List<ComponentViewModel> { groupVm };

        // Recalculate routes for the new components
        _ = _canvas.RecalculateRoutesAsync();
    }

    public void Undo()
    {
        if (_groupToPlace == null || _placedComponentViewModels == null || _placedComponentViewModels.Count == 0)
            return;

        // Remove the group from canvas (this will handle child cleanup)
        _canvas.RemoveComponent(_placedComponentViewModels[0]);

        _placedComponentViewModels = null;

        // Recalculate routes after removal
        _ = _canvas.RecalculateRoutesAsync();
    }
}
