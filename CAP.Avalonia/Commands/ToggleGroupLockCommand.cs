using CAP_Core.Components.Core;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for toggling a ComponentGroup's lock state.
/// Provides undo/redo support for lock/unlock operations on groups.
/// </summary>
public class ToggleGroupLockCommand : IUndoableCommand
{
    private readonly ComponentGroup _group;
    private readonly bool _wasLocked;

    /// <summary>
    /// Creates a command to toggle a group's lock state.
    /// </summary>
    /// <param name="group">The group to toggle lock state for.</param>
    public ToggleGroupLockCommand(ComponentGroup group)
    {
        _group = group ?? throw new ArgumentNullException(nameof(group));
        _wasLocked = group.IsLocked;
    }

    public string Description =>
        _wasLocked
            ? $"Unlock group '{_group.GroupName}'"
            : $"Lock group '{_group.GroupName}'";

    public void Execute()
    {
        _group.IsLocked = !_wasLocked;
    }

    public void Undo()
    {
        _group.IsLocked = _wasLocked;
    }
}
