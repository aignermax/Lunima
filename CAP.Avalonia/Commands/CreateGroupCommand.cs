using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.ComponentHelpers;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command to create a ComponentGroup from selected components and save to catalog.
/// This operation is undoable - undo removes the group from the catalog.
/// </summary>
public class CreateGroupCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentGroupViewModel _groupViewModel;
    private readonly List<ComponentViewModel> _selectedComponents;
    private readonly string _groupName;
    private readonly string _category;
    private readonly string _description;
    private ComponentGroup? _createdGroup;

    /// <summary>
    /// Creates a new create group command.
    /// </summary>
    /// <param name="canvas">The design canvas containing selected components.</param>
    /// <param name="groupViewModel">The component group library ViewModel.</param>
    /// <param name="selectedComponents">Components to include in the group.</param>
    /// <param name="groupName">Display name for the group.</param>
    /// <param name="category">Category for library organization.</param>
    /// <param name="description">Optional description.</param>
    public CreateGroupCommand(
        DesignCanvasViewModel canvas,
        ComponentGroupViewModel groupViewModel,
        IReadOnlyList<ComponentViewModel> selectedComponents,
        string groupName,
        string category = "User Defined",
        string description = "")
    {
        _canvas = canvas;
        _groupViewModel = groupViewModel;
        _selectedComponents = selectedComponents.ToList();
        _groupName = groupName;
        _category = category;
        _description = description;
    }

    /// <inheritdoc />
    public string Description => $"Create group '{_groupName}' from {_selectedComponents.Count} components";

    /// <inheritdoc />
    public void Execute()
    {
        // Get the actual Component objects
        var components = _selectedComponents.Select(vm => vm.Component).ToList();

        // Get connections between selected components (internal connections)
        var componentSet = new HashSet<Component>(components);
        var connections = _canvas.Connections
            .Where(connVm =>
                componentSet.Contains(connVm.Connection.StartPin.ParentComponent) &&
                componentSet.Contains(connVm.Connection.EndPin.ParentComponent))
            .Select(connVm => connVm.Connection)
            .ToList();

        // Create the group using ComponentGroupManager
        // We create a temporary manager here to build the group definition
        var catalogPath = GetCatalogPath();
        var tempManager = new ComponentGroupManager(catalogPath);

        _createdGroup = tempManager.CreateGroupFromComponents(_groupName, _category, components, connections);
        _createdGroup.Description = _description;

        // Save using the ViewModel's method, which will use its internal manager
        // and refresh the UI properly
        _groupViewModel.SaveGroup(_createdGroup);
    }

    /// <inheritdoc />
    public void Undo()
    {
        if (_createdGroup == null)
            return;

        // Find the group in the ViewModel's available groups and delete it
        var groupToDelete = _groupViewModel.AvailableGroups.FirstOrDefault(g => g.Id == _createdGroup.Id);
        if (groupToDelete != null)
        {
            _groupViewModel.SelectedGroup = groupToDelete;
            _groupViewModel.DeleteSelectedGroupCommand.Execute(null);
        }
    }

    private static string GetCatalogPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ConnectAPicPro",
            "component-groups.json");
    }
}
