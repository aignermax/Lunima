using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Grid;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// ViewModel for locking/unlocking components and connections.
/// Provides commands to lock selected elements to prevent accidental modification.
/// </summary>
public partial class ElementLockViewModel : ObservableObject
{
    private readonly LockManager _lockManager;

    [ObservableProperty]
    private DesignCanvasViewModel? _canvas;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private int _lockedComponentCount;

    [ObservableProperty]
    private int _lockedConnectionCount;

    public ElementLockViewModel()
    {
        _lockManager = new LockManager();
    }

    /// <summary>
    /// Configures the ViewModel with the design canvas.
    /// </summary>
    public void Configure(DesignCanvasViewModel canvas)
    {
        Canvas = canvas;

        // Subscribe to selection changes to update command availability
        Canvas.Selection.SelectedComponents.CollectionChanged += (s, e) =>
        {
            RefreshCommands();
        };

        UpdateLockCounts();
    }

    /// <summary>
    /// Locks the selected components.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLockSelectedComponents))]
    private void LockSelectedComponents()
    {
        if (Canvas == null)
            return;

        var selectedComponents = Canvas.Selection.SelectedComponents
            .Select(vm => vm.Component)
            .Where(c => !c.IsLocked)
            .ToList();

        if (selectedComponents.Any())
        {
            _lockManager.LockComponents(selectedComponents);
            StatusText = $"Locked {selectedComponents.Count} component(s)";
            UpdateLockCounts();
            RefreshCommands(); // Update button states immediately
        }
    }

    private bool CanLockSelectedComponents()
    {
        return Canvas?.Selection.SelectedComponents.Any(vm => !vm.Component.IsLocked) ?? false;
    }

    /// <summary>
    /// Unlocks the selected components.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUnlockSelectedComponents))]
    private void UnlockSelectedComponents()
    {
        if (Canvas == null)
            return;

        var selectedComponents = Canvas.Selection.SelectedComponents
            .Select(vm => vm.Component)
            .Where(c => c.IsLocked)
            .ToList();

        if (selectedComponents.Any())
        {
            _lockManager.UnlockComponents(selectedComponents);
            StatusText = $"Unlocked {selectedComponents.Count} component(s)";
            UpdateLockCounts();
            RefreshCommands(); // Update button states immediately
        }
    }

    private bool CanUnlockSelectedComponents()
    {
        return Canvas?.Selection.SelectedComponents.Any(vm => vm.Component.IsLocked) ?? false;
    }

    /// <summary>
    /// Toggles the lock state of selected components.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedComponents))]
    private void ToggleSelectedComponents()
    {
        if (Canvas == null)
            return;

        var selectedComponents = Canvas.Selection.SelectedComponents
            .Select(vm => vm.Component)
            .ToList();

        if (selectedComponents.Any())
        {
            foreach (var component in selectedComponents)
            {
                _lockManager.ToggleComponentLock(component);
            }

            StatusText = $"Toggled lock for {selectedComponents.Count} component(s)";
            UpdateLockCounts();
        }
    }

    private bool HasSelectedComponents()
    {
        return Canvas?.Selection.SelectedComponents.Any() ?? false;
    }

    /// <summary>
    /// Unlocks all components in the design.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasLockedComponents))]
    private void UnlockAllComponents()
    {
        if (Canvas == null)
            return;

        var allComponents = Canvas.Components.Select(vm => vm.Component).ToList();
        var lockedComponents = _lockManager.GetLockedComponents(allComponents).ToList();

        if (lockedComponents.Any())
        {
            _lockManager.UnlockComponents(lockedComponents);
            StatusText = $"Unlocked all {lockedComponents.Count} component(s)";
            UpdateLockCounts();
        }
    }

    private bool HasLockedComponents()
    {
        if (Canvas == null)
            return false;

        return Canvas.Components.Any(vm => vm.Component.IsLocked);
    }

    /// <summary>
    /// Locks selected connections.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedConnections))]
    private void LockSelectedConnections()
    {
        if (Canvas == null)
            return;

        // Note: Connection selection would need to be implemented in the canvas
        // For now, this is a placeholder for future connection selection support
        StatusText = "Connection selection not yet implemented";
    }

    private bool HasSelectedConnections()
    {
        // Placeholder for connection selection
        return false;
    }

    /// <summary>
    /// Unlocks all connections in the design.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasLockedConnections))]
    private void UnlockAllConnections()
    {
        if (Canvas == null)
            return;

        var lockedConnections = Canvas.Connections
            .Select(vm => vm.Connection)
            .Where(c => c.IsLocked)
            .ToList();

        if (lockedConnections.Any())
        {
            _lockManager.UnlockConnections(lockedConnections);
            StatusText = $"Unlocked all {lockedConnections.Count} connection(s)";
            UpdateLockCounts();
        }
    }

    private bool HasLockedConnections()
    {
        if (Canvas == null)
            return false;

        return Canvas.Connections.Any(vm => vm.Connection.IsLocked);
    }

    /// <summary>
    /// Updates the count of locked elements for display.
    /// </summary>
    private void UpdateLockCounts()
    {
        if (Canvas == null)
        {
            LockedComponentCount = 0;
            LockedConnectionCount = 0;
            return;
        }

        LockedComponentCount = Canvas.Components.Count(vm => vm.Component.IsLocked);
        LockedConnectionCount = Canvas.Connections.Count(vm => vm.Connection.IsLocked);
    }

    /// <summary>
    /// Refreshes command availability states.
    /// Call this when selection changes or lock states change externally.
    /// </summary>
    public void RefreshCommands()
    {
        UpdateLockCounts();
        LockSelectedComponentsCommand.NotifyCanExecuteChanged();
        UnlockSelectedComponentsCommand.NotifyCanExecuteChanged();
        ToggleSelectedComponentsCommand.NotifyCanExecuteChanged();
        UnlockAllComponentsCommand.NotifyCanExecuteChanged();
        LockSelectedConnectionsCommand.NotifyCanExecuteChanged();
        UnlockAllConnectionsCommand.NotifyCanExecuteChanged();
    }
}
