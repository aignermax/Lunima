using System.Collections.ObjectModel;
using CAP_Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.Services;
using CAP_Core.Components.Creation;
using CAP_Core.Components;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// ViewModel for the left sidebar panel.
/// Contains hierarchy panel, component library management, and PDK loading.
/// Max 250 lines per CLAUDE.md guideline.
/// </summary>
public partial class LeftPanelViewModel : ObservableObject
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly PdkLoader _pdkLoader;
    private readonly UserPreferencesService _preferencesService;
    private readonly ErrorConsoleService? _errorConsole;

    /// <summary>
    /// ViewModel for the hierarchy panel showing component tree structure.
    /// </summary>
    public HierarchyPanelViewModel HierarchyPanel { get; }

    /// <summary>
    /// ViewModel for PDK management (loading, filtering, enabling/disabling PDKs).
    /// </summary>
    public PdkManagerViewModel PdkManager { get; }

    /// <summary>
    /// ViewModel for managing saved ComponentGroup templates.
    /// </summary>
    public ComponentLibraryViewModel ComponentLibrary { get; }

    /// <summary>
    /// All component templates (built-in + PDK).
    /// </summary>
    public ObservableCollection<ComponentTemplate> AllTemplates { get; } = new();

    /// <summary>
    /// Filtered component templates based on search and PDK filters.
    /// </summary>
    public ObservableCollection<ComponentTemplate> FilteredTemplates { get; } = new();

    /// <summary>
    /// Available component categories.
    /// </summary>
    public ObservableCollection<string> Categories { get; } = new();

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private double _libraryScrollOffset = 0.0;

    [ObservableProperty]
    private GroupTemplate? _selectedGroupTemplate;

    private GridLength _leftPanelWidth = new GridLength(220);
    /// <summary>
    /// Width of the left panel in pixels. Persisted in user preferences.
    /// Clamped to [200, 800] range.
    /// </summary>
    public GridLength LeftPanelWidth
    {
        get => _leftPanelWidth;
        set
        {
            // Clamp to reasonable values (min 200, max 800)
            var clampedValue = Math.Max(200, Math.Min(800, value.Value));
            var newGridLength = new GridLength(clampedValue);
            if (SetProperty(ref _leftPanelWidth, newGridLength))
            {
                SaveLeftPanelWidth();
            }
        }
    }

    /// <summary>
    /// Callback to update status text in the UI.
    /// </summary>
    public Action<string>? UpdateStatus { get; set; }

    /// <summary>
    /// File dialog service for loading PDK files.
    /// </summary>
    public IFileDialogService? FileDialogService { get; set; }

    /// <summary>
    /// Callback invoked when a group template is selected for placement.
    /// </summary>
    public Action<GroupTemplate>? OnGroupTemplateSelected { get; set; }

    /// <summary>Initializes a new instance of <see cref="LeftPanelViewModel"/>.</summary>
    public LeftPanelViewModel(
        DesignCanvasViewModel canvas,
        GroupLibraryManager libraryManager,
        PdkLoader pdkLoader,
        UserPreferencesService preferencesService,
        HierarchyPanelViewModel hierarchyPanel,
        PdkManagerViewModel pdkManager,
        ComponentLibraryViewModel componentLibrary,
        ErrorConsoleService? errorConsole = null)
    {
        _canvas = canvas;
        _pdkLoader = pdkLoader;
        _preferencesService = preferencesService;
        _errorConsole = errorConsole;

        HierarchyPanel = hierarchyPanel;
        PdkManager = pdkManager;
        ComponentLibrary = componentLibrary;

        PdkManager.OnFilterChanged = FilterComponents;
    }

    /// <summary>
    /// Initializes the component library (loads built-in + bundled PDKs).
    /// </summary>
    public void Initialize()
    {
        LoadComponentLibrary();
        RestorePdkFilterState();
        RestoreLeftPanelWidth();
    }

    partial void OnSearchTextChanged(string value) => FilterComponents();

    partial void OnSelectedGroupTemplateChanged(GroupTemplate? value)
    {
        if (value != null)
        {
            OnGroupTemplateSelected?.Invoke(value);
        }
    }

    private void LoadComponentLibrary()
    {
        // All components are loaded from JSON PDK files — no hardcoded built-in templates.
        LoadBundledPdks();

        // Build category list from all loaded templates
        var categories = AllTemplates.Select(t => t.Category).Distinct().OrderBy(c => c);
        foreach (var category in categories)
        {
            Categories.Add(category);
        }

        UpdateStatus?.Invoke($"Loaded {AllTemplates.Count} component types");
        FilterComponents();
    }

    private void LoadBundledPdks()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var pdkDir = Path.Combine(baseDir, "PDKs");

        if (!Directory.Exists(pdkDir))
            return;

        foreach (var pdkFile in Directory.GetFiles(pdkDir, "*.json"))
        {
            try
            {
                var pdk = _pdkLoader.LoadFromFile(pdkFile);
                int componentCount = 0;
                foreach (var pdkComp in pdk.Components)
                {
                    var template = ConvertPdkComponentToTemplate(pdkComp, pdk.Name, pdk.NazcaModuleName);
                    AllTemplates.Add(template);
                    componentCount++;
                }

                PdkManager.RegisterPdk(pdk.Name, pdkFile, true, componentCount);
            }
            catch (CAP_DataAccess.Components.ComponentDraftMapper.PdkValidationException vex)
            {
                foreach (var error in vex.Errors)
                {
                    _errorConsole?.LogError($"PDK validation: {error}");
                }
                _errorConsole?.LogError($"Skipped PDK '{Path.GetFileName(pdkFile)}' — {vex.Errors.Count} validation error(s)");
            }
            catch (Exception ex)
            {
                _errorConsole?.LogError($"Failed to load PDK '{Path.GetFileName(pdkFile)}': {ex.Message}");
            }
        }
    }

    private void FilterComponents()
    {
        FilteredTemplates.Clear();
        var query = SearchText?.Trim() ?? "";
        var enabledPdks = PdkManager.GetEnabledPdkNames();

        foreach (var t in AllTemplates)
        {
            if (!enabledPdks.Contains(t.PdkSource))
                continue;

            if (query.Length == 0 || MatchesSearch(t, query))
                FilteredTemplates.Add(t);
        }

        SavePdkFilterState();
    }

    private static bool MatchesSearch(ComponentTemplate t, string query)
    {
        return t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || t.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (t.NazcaFunctionName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            || t.PdkSource.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void RestorePdkFilterState()
    {
        var enabledPdks = _preferencesService.GetEnabledPdks();

        if (enabledPdks.Count == 0)
            return;

        foreach (var pdk in PdkManager.LoadedPdks)
        {
            pdk.IsEnabled = enabledPdks.Contains(pdk.Name);
        }

        FilterComponents();
    }

    private void SavePdkFilterState()
    {
        var enabledPdks = PdkManager.GetEnabledPdkNames();
        _preferencesService.SetEnabledPdks(enabledPdks);
    }

    private void RestoreLeftPanelWidth()
    {
        var width = _preferencesService.GetLeftPanelWidth();
        LeftPanelWidth = new GridLength(width);
    }

    private void SaveLeftPanelWidth()
    {
        _preferencesService.SetLeftPanelWidth(LeftPanelWidth.Value);
    }

    [RelayCommand]
    private async Task LoadPdk()
    {
        if (FileDialogService == null) return;

        var filePath = await FileDialogService.ShowOpenFileDialogAsync(
            "Open PDK",
            "PDK Files (*.json)|*.json|All Files (*.*)|*.*");

        if (string.IsNullOrEmpty(filePath)) return;

        if (PdkManager.IsPdkLoaded(filePath))
        {
            UpdateStatus?.Invoke("PDK already loaded from this file");
            return;
        }

        try
        {
            var pdk = _pdkLoader.LoadFromFile(filePath);

            if (PdkManager.IsPdkNameLoaded(pdk.Name, null))
            {
                UpdateStatus?.Invoke($"PDK '{pdk.Name}' is already loaded");
                return;
            }

            int addedCount = 0;
            foreach (var pdkComp in pdk.Components)
            {
                var template = ConvertPdkComponentToTemplate(pdkComp, pdk.Name, pdk.NazcaModuleName);
                AllTemplates.Add(template);
                if (!Categories.Contains(template.Category))
                    Categories.Add(template.Category);
                addedCount++;
            }

            PdkManager.RegisterPdk(pdk.Name, filePath, false, addedCount);
            _preferencesService.AddUserPdkPath(filePath);

            FilterComponents();
            UpdateStatus?.Invoke($"Loaded PDK '{pdk.Name}' with {addedCount} components");
        }
        catch (Exception ex)
        {
            _errorConsole?.LogError($"Failed to load PDK: {ex.Message}", ex);
            UpdateStatus?.Invoke($"Failed to load PDK: {ex.Message}");
        }
    }

    private static ComponentTemplate ConvertPdkComponentToTemplate(PdkComponentDraft pdkComp, string pdkName, string? nazcaModuleName)
        => PdkTemplateConverter.ConvertToTemplate(pdkComp, pdkName, nazcaModuleName);
}
