using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Commands;

/// <summary>Marks (or unmarks) a component as a user-defined light source, undoably.</summary>
public class MarkLightSourceCommand : IUndoableCommand
{
    private readonly ComponentViewModel _component;
    private readonly bool _before;
    private readonly bool _after;

    /// <summary>Creates the command that sets the mark to <paramref name="marked"/>.</summary>
    public MarkLightSourceCommand(ComponentViewModel component, bool marked)
    {
        _component = component ?? throw new ArgumentNullException(nameof(component));
        _before = component.IsUserMarkedLightSource;
        _after = marked;
    }

    /// <inheritdoc/>
    public string Description => _after
        ? $"Mark '{_component.Name}' as light source"
        : $"Unmark '{_component.Name}' as light source";

    /// <inheritdoc/>
    public void Execute() => _component.IsUserMarkedLightSource = _after;

    /// <inheritdoc/>
    public void Undo() => _component.IsUserMarkedLightSource = _before;
}
