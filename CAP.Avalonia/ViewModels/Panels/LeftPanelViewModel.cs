using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CAP.Avalonia.ViewModels.Library;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// ViewModel for the left panel containing component library, search, and PDK management.
/// </summary>
public partial class LeftPanelViewModel : ObservableObject
{
    /// <summary>
    /// Full component library (all loaded templates from built-in and PDKs).
    /// </summary>
    public ObservableCollection<ComponentTemplate> ComponentLibrary { get; } = new();

    /// <summary>
    /// Filtered component library based on search text and enabled PDKs.
    /// </summary>
    public ObservableCollection<ComponentTemplate> FilteredComponentLibrary { get; } = new();

    /// <summary>
    /// Available component categories.
    /// </summary>
    public ObservableCollection<string> Categories { get; } = new();

    /// <summary>
    /// Search text for filtering components.
    /// </summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>
    /// Currently selected component template for placement.
    /// </summary>
    [ObservableProperty]
    private ComponentTemplate? _selectedTemplate;

    /// <summary>
    /// PDK manager for loading and filtering PDKs.
    /// </summary>
    public PdkManagerViewModel PdkManager { get; } = new();

    /// <summary>
    /// Element lock manager for locking/unlocking components and connections.
    /// </summary>
    public ElementLockViewModel ElementLock { get; } = new();

    /// <summary>
    /// Component group manager for saving and loading user-defined component groups.
    /// </summary>
    public ComponentGroupViewModel ComponentGroups { get; } = new();

    /// <summary>
    /// Hierarchy panel showing component tree structure.
    /// </summary>
    public HierarchyPanelViewModel HierarchyPanel { get; } = new();

    /// <summary>
    /// Callback to trigger component filtering when search text or PDK filter changes.
    /// Set by MainViewModel after initialization.
    /// </summary>
    public Action? OnFilterChanged { get; set; }

    partial void OnSearchTextChanged(string value)
    {
        OnFilterChanged?.Invoke();
    }

    /// <summary>
    /// Filters the component library based on search text and enabled PDKs.
    /// </summary>
    /// <param name="enabledPdks">Set of enabled PDK names.</param>
    public void FilterComponents(HashSet<string> enabledPdks)
    {
        FilteredComponentLibrary.Clear();
        var query = SearchText?.Trim() ?? "";

        foreach (var t in ComponentLibrary)
        {
            // Filter by PDK enabled state
            if (!enabledPdks.Contains(t.PdkSource))
                continue;

            // Filter by search query
            if (query.Length == 0 || MatchesSearch(t, query))
                FilteredComponentLibrary.Add(t);
        }
    }

    private static bool MatchesSearch(ComponentTemplate t, string query)
    {
        return t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || t.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (t.NazcaFunctionName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            || t.PdkSource.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
