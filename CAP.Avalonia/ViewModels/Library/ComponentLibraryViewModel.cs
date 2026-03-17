using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Components.Creation;

namespace CAP.Avalonia.ViewModels.Library;

/// <summary>
/// ViewModel for managing the component library including saved ComponentGroup templates.
/// Handles user groups and PDK macro groups separately for easy filtering.
/// </summary>
public partial class ComponentLibraryViewModel : ObservableObject
{
    private readonly GroupLibraryManager _libraryManager;

    [ObservableProperty]
    private string _statusText = "No groups loaded";

    /// <summary>
    /// User-created group templates.
    /// </summary>
    public ObservableCollection<GroupTemplate> UserGroups { get; } = new();

    /// <summary>
    /// PDK-provided group templates (macros).
    /// </summary>
    public ObservableCollection<GroupTemplate> PdkGroups { get; } = new();

    /// <summary>
    /// Currently selected group template for drag-and-drop.
    /// </summary>
    [ObservableProperty]
    private GroupTemplate? _selectedGroupTemplate;

    /// <summary>
    /// Initializes the component library ViewModel.
    /// </summary>
    /// <param name="libraryManager">The group library manager service.</param>
    public ComponentLibraryViewModel(GroupLibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
        LoadGroups();
    }

    /// <summary>
    /// Loads all group templates from the library.
    /// </summary>
    [RelayCommand]
    private void LoadGroups()
    {
        UserGroups.Clear();
        PdkGroups.Clear();

        _libraryManager.LoadTemplates();

        foreach (var template in _libraryManager.UserTemplates)
        {
            UserGroups.Add(template);
        }

        foreach (var template in _libraryManager.PdkTemplates)
        {
            PdkGroups.Add(template);
        }

        UpdateStatus();
    }

    /// <summary>
    /// Adds a new group template to the library.
    /// </summary>
    /// <param name="template">The template to add.</param>
    public void AddTemplate(GroupTemplate template)
    {
        if (template.Source == "User")
        {
            UserGroups.Add(template);
        }
        else
        {
            PdkGroups.Add(template);
        }

        UpdateStatus();
    }

    /// <summary>
    /// Removes a group template from the library.
    /// </summary>
    /// <param name="template">The template to remove.</param>
    [RelayCommand]
    private void RemoveTemplate(GroupTemplate template)
    {
        if (_libraryManager.RemoveTemplate(template))
        {
            UserGroups.Remove(template);
            PdkGroups.Remove(template);
            UpdateStatus();
        }
    }

    /// <summary>
    /// Removes a group template from the library by name.
    /// </summary>
    /// <param name="templateName">The name of the template to remove.</param>
    public void RemoveTemplateByName(string templateName)
    {
        var template = UserGroups.FirstOrDefault(t => t.Name == templateName)
                      ?? PdkGroups.FirstOrDefault(t => t.Name == templateName);

        if (template != null)
        {
            RemoveTemplate(template);
        }
    }

    /// <summary>
    /// Duplicates a group template with a new name.
    /// </summary>
    /// <param name="template">The template to duplicate.</param>
    [RelayCommand]
    private void DuplicateTemplate(GroupTemplate template)
    {
        if (template.TemplateGroup == null)
            return;

        var duplicateName = $"{template.Name}_Copy";
        var duplicated = _libraryManager.SaveTemplate(
            template.TemplateGroup,
            duplicateName,
            template.Description,
            template.Source);

        AddTemplate(duplicated);
    }

    /// <summary>
    /// Updates the status text based on loaded templates.
    /// </summary>
    private void UpdateStatus()
    {
        int userCount = UserGroups.Count;
        int pdkCount = PdkGroups.Count;
        int total = userCount + pdkCount;

        if (total == 0)
        {
            StatusText = "No saved groups";
        }
        else
        {
            StatusText = $"{userCount} user group(s), {pdkCount} PDK macro(s)";
        }
    }

    /// <summary>
    /// Gets the underlying library manager for advanced operations.
    /// </summary>
    public GroupLibraryManager GetLibraryManager() => _libraryManager;
}
