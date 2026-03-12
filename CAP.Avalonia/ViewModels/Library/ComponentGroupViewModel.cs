using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Components.ComponentHelpers;

namespace CAP.Avalonia.ViewModels.Library;

/// <summary>
/// ViewModel for managing user-defined component groups in the library.
/// </summary>
public partial class ComponentGroupViewModel : ObservableObject
{
    private readonly ComponentGroupManager _groupManager;
    private readonly string _catalogPath;

    /// <summary>
    /// All available component groups in the catalog.
    /// </summary>
    public ObservableCollection<ComponentGroupInfo> AvailableGroups { get; } = new();

    /// <summary>
    /// Currently selected group for viewing or placement.
    /// </summary>
    [ObservableProperty]
    private ComponentGroupInfo? _selectedGroup;

    /// <summary>
    /// Status text showing catalog info.
    /// </summary>
    [ObservableProperty]
    private string _statusText = "No groups saved";

    /// <summary>
    /// Search filter for groups.
    /// </summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>
    /// Name for new group being created.
    /// </summary>
    [ObservableProperty]
    private string _newGroupName = "";

    /// <summary>
    /// Category for new group being created.
    /// </summary>
    [ObservableProperty]
    private string _newGroupCategory = "User Defined";

    /// <summary>
    /// Description for new group being created.
    /// </summary>
    [ObservableProperty]
    private string _newGroupDescription = "";

    /// <summary>
    /// Callback to invoke when user wants to create a group from selection.
    /// Set by MainViewModel.
    /// </summary>
    public Action<string, string, string>? OnCreateGroupFromSelection { get; set; }

    /// <summary>
    /// Callback to invoke when user wants to place a group on the canvas.
    /// Set by MainViewModel.
    /// </summary>
    public Action<ComponentGroup>? OnPlaceGroup { get; set; }

    public ComponentGroupViewModel()
    {
        // Default catalog path in user's AppData
        _catalogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ConnectAPicPro",
            "component-groups.json");

        _groupManager = new ComponentGroupManager(_catalogPath);
        RefreshGroups();
    }

    /// <summary>
    /// Refreshes the list of available groups from the catalog.
    /// </summary>
    [RelayCommand]
    private void RefreshGroups()
    {
        AvailableGroups.Clear();
        foreach (var group in _groupManager.Groups)
        {
            AvailableGroups.Add(new ComponentGroupInfo(group));
        }

        UpdateStatusText();
    }

    /// <summary>
    /// Creates a new group from the current selection.
    /// </summary>
    [RelayCommand]
    private void CreateGroup()
    {
        if (string.IsNullOrWhiteSpace(NewGroupName))
        {
            StatusText = "Error: Group name cannot be empty";
            return;
        }

        OnCreateGroupFromSelection?.Invoke(NewGroupName, NewGroupCategory, NewGroupDescription);

        // Clear input fields
        NewGroupName = "";
        NewGroupCategory = "User Defined";
        NewGroupDescription = "";
    }

    /// <summary>
    /// Saves a newly created group to the catalog.
    /// </summary>
    public void SaveGroup(ComponentGroup group)
    {
        _groupManager.SaveGroup(group);
        RefreshGroups();
        StatusText = $"Saved group '{group.Name}' ({group.Components.Count} components)";
    }

    /// <summary>
    /// Deletes the currently selected group.
    /// </summary>
    [RelayCommand]
    private void DeleteSelectedGroup()
    {
        if (SelectedGroup == null)
        {
            StatusText = "No group selected";
            return;
        }

        _groupManager.DeleteGroup(SelectedGroup.Id);
        RefreshGroups();
        StatusText = $"Deleted group '{SelectedGroup.Name}'";
        SelectedGroup = null;
    }

    /// <summary>
    /// Places the selected group on the canvas.
    /// </summary>
    [RelayCommand]
    private void PlaceSelectedGroup()
    {
        if (SelectedGroup == null)
        {
            StatusText = "No group selected";
            return;
        }

        var group = _groupManager.Groups.FirstOrDefault(g => g.Id == SelectedGroup.Id);
        if (group != null)
        {
            OnPlaceGroup?.Invoke(group);
            StatusText = $"Click canvas to place '{group.Name}'";
        }
    }

    private void UpdateStatusText()
    {
        var count = AvailableGroups.Count;
        StatusText = count == 0
            ? "No groups saved"
            : $"{count} group{(count == 1 ? "" : "s")} in catalog";
    }

    partial void OnSearchTextChanged(string value)
    {
        // TODO: Implement filtering if needed
    }
}

/// <summary>
/// Display information for a component group in the library.
/// </summary>
public class ComponentGroupInfo
{
    public Guid Id { get; }
    public string Name { get; }
    public string Category { get; }
    public string Description { get; }
    public int ComponentCount { get; }
    public int ConnectionCount { get; }
    public double WidthMicrometers { get; }
    public double HeightMicrometers { get; }
    public string CreatedAtText { get; }

    public ComponentGroupInfo(ComponentGroup group)
    {
        Id = group.Id;
        Name = group.Name;
        Category = group.Category;
        Description = group.Description;
        ComponentCount = group.Components.Count;
        ConnectionCount = group.Connections.Count;
        WidthMicrometers = group.WidthMicrometers;
        HeightMicrometers = group.HeightMicrometers;
        CreatedAtText = group.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd");
    }
}
