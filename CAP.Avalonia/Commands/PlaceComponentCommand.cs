using CAP.Avalonia.ViewModels;
using CAP_Core.Components;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for placing a new component on the canvas.
/// </summary>
public class PlaceComponentCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly Component _component;
    private readonly ComponentTemplate _template;
    private readonly double _x;
    private readonly double _y;
    private ComponentViewModel? _createdViewModel;

    public PlaceComponentCommand(
        DesignCanvasViewModel canvas,
        ComponentTemplate template,
        double x,
        double y)
    {
        _canvas = canvas;
        _template = template;
        _x = x;
        _y = y;
        _component = ComponentTemplates.CreateFromTemplate(template, x, y);
    }

    public string Description => $"Place {_template.Name}";

    public void Execute()
    {
        _createdViewModel = _canvas.AddComponent(_component, _template.Name);
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
