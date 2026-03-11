using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Grid;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for unlocking components to allow modification.
/// </summary>
public class UnlockComponentsCommand : IUndoableCommand
{
    private readonly LockManager _lockManager;
    private readonly List<Component> _components;
    private readonly DesignCanvasViewModel? _canvas;

    public UnlockComponentsCommand(LockManager lockManager, List<Component> components, DesignCanvasViewModel? canvas = null)
    {
        _lockManager = lockManager;
        _components = components;
        _canvas = canvas;
    }

    public string Description
    {
        get
        {
            if (_components.Count == 1)
                return $"Unlock {_components[0].Identifier}";
            return $"Unlock {_components.Count} components";
        }
    }

    public void Execute()
    {
        _lockManager.UnlockComponents(_components);
        NotifyViewModels();
    }

    public void Undo()
    {
        _lockManager.LockComponents(_components);
        NotifyViewModels();
    }

    private void NotifyViewModels()
    {
        if (_canvas == null) return;

        foreach (var component in _components)
        {
            var vm = _canvas.Components.FirstOrDefault(c => c.Component == component);
            vm?.NotifyLockStateChanged();
        }
    }
}
