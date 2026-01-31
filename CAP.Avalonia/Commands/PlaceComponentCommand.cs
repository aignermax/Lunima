using CAP.Avalonia.ViewModels;
using CAP_Core.Components;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for placing a new component on the canvas.
/// Returns null from TryCreate if no valid placement position exists.
/// </summary>
public class PlaceComponentCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly Component? _component;
    private readonly ComponentTemplate _template;
    private readonly double _x;
    private readonly double _y;
    private readonly bool _isValid;
    private ComponentViewModel? _createdViewModel;

    private PlaceComponentCommand(
        DesignCanvasViewModel canvas,
        ComponentTemplate template,
        double x,
        double y,
        bool isValid)
    {
        _canvas = canvas;
        _template = template;
        _x = x;
        _y = y;
        _isValid = isValid;

        if (isValid)
        {
            _component = ComponentTemplates.CreateFromTemplate(template, _x, _y);
        }
    }

    /// <summary>
    /// Tries to create a placement command. Returns null if no valid position exists.
    /// </summary>
    public static PlaceComponentCommand? TryCreate(
        DesignCanvasViewModel canvas,
        ComponentTemplate template,
        double x,
        double y)
    {
        var validPosition = canvas.FindValidPlacement(x, y, template.WidthMicrometers, template.HeightMicrometers);

        if (validPosition == null)
        {
            return null; // No space available
        }

        return new PlaceComponentCommand(canvas, template, validPosition.Value.x, validPosition.Value.y, true);
    }

    public string Description => $"Place {_template.Name}";

    public void Execute()
    {
        if (_isValid && _component != null)
        {
            _createdViewModel = _canvas.AddComponent(_component, _template.Name);
        }
    }

    public void Undo()
    {
        if (_createdViewModel != null)
        {
            _canvas.RemoveComponent(_createdViewModel);
            _createdViewModel = null;
        }
    }
}
