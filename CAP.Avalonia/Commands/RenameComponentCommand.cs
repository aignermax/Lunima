using CAP_Core.Components.Core;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command to rename a component or group in the Hierarchy Panel.
/// For regular components, updates <see cref="Component.HumanReadableName"/>.
/// For groups, updates <see cref="ComponentGroup.GroupName"/>.
/// Supports undo/redo.
/// </summary>
public class RenameComponentCommand : IUndoableCommand
{
    private readonly Component _component;
    private readonly string _newName;
    private readonly string? _oldHumanReadableName;
    private readonly string _oldGroupName;
    private readonly bool _isGroup;

    /// <inheritdoc/>
    public string Description => $"Rename to '{_newName}'";

    /// <summary>
    /// Initializes a new instance of <see cref="RenameComponentCommand"/>.
    /// </summary>
    /// <param name="component">The component or group to rename.</param>
    /// <param name="newName">The new display name to apply.</param>
    public RenameComponentCommand(Component component, string newName)
    {
        _component = component ?? throw new ArgumentNullException(nameof(component));
        _newName = newName ?? throw new ArgumentNullException(nameof(newName));
        _isGroup = component is ComponentGroup;

        if (_isGroup && component is ComponentGroup group)
            _oldGroupName = group.GroupName;
        else
        {
            _oldGroupName = string.Empty;
            _oldHumanReadableName = component.HumanReadableName;
        }
    }

    /// <inheritdoc/>
    public void Execute()
    {
        if (_isGroup && _component is ComponentGroup group)
            group.GroupName = _newName;
        else
            _component.HumanReadableName = _newName;
    }

    /// <inheritdoc/>
    public void Undo()
    {
        if (_isGroup && _component is ComponentGroup group)
            group.GroupName = _oldGroupName;
        else
            _component.HumanReadableName = _oldHumanReadableName;
    }
}
