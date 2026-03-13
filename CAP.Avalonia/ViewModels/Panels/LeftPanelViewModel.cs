using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.Services;
using CAP_Core.Components.Creation;
using CAP_Core.LightCalculation;
using CAP_Core.Components;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using System.Numerics;

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

    /// <summary>
    /// Callback to update status text in the UI.
    /// </summary>
    public Action<string>? UpdateStatus { get; set; }

    /// <summary>
    /// File dialog service for loading PDK files.
    /// </summary>
    public IFileDialogService? FileDialogService { get; set; }

    public LeftPanelViewModel(
        DesignCanvasViewModel canvas,
        GroupLibraryManager libraryManager,
        PdkLoader pdkLoader,
        UserPreferencesService preferencesService)
    {
        _canvas = canvas;
        _pdkLoader = pdkLoader;
        _preferencesService = preferencesService;

        HierarchyPanel = new HierarchyPanelViewModel(canvas);
        PdkManager = new PdkManagerViewModel();
        ComponentLibrary = new ComponentLibraryViewModel(libraryManager);

        PdkManager.OnFilterChanged = FilterComponents;
    }

    /// <summary>
    /// Initializes the component library (loads built-in + bundled PDKs).
    /// </summary>
    public void Initialize()
    {
        LoadComponentLibrary();
        RestorePdkFilterState();
    }

    partial void OnSearchTextChanged(string value) => FilterComponents();

    private void LoadComponentLibrary()
    {
        var templates = ComponentTemplates.GetAllTemplates();

        foreach (var template in templates)
        {
            AllTemplates.Add(template);
        }

        // Register built-in templates as a PDK
        if (templates.Count > 0)
        {
            PdkManager.RegisterPdk("Built-in Components", null, true, templates.Count);
        }

        // Auto-load bundled PDK files from PDKs directory
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
            catch
            {
                // Skip malformed PDK files silently at startup
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
            UpdateStatus?.Invoke($"Failed to load PDK: {ex.Message}");
        }
    }

    private ComponentTemplate ConvertPdkComponentToTemplate(PdkComponentDraft pdkComp, string pdkName, string? nazcaModuleName)
    {
        var pinDefs = pdkComp.Pins.Select(p => new PinDefinition(
            p.Name,
            p.OffsetXMicrometers,
            p.OffsetYMicrometers,
            p.AngleDegrees
        )).ToArray();

        var firstPin = pdkComp.Pins.FirstOrDefault();
        double nazcaOriginOffsetX = firstPin?.OffsetXMicrometers ?? 0;
        double nazcaOriginOffsetY = firstPin?.OffsetYMicrometers ?? 0;

        var template = new ComponentTemplate
        {
            Name = pdkComp.Name,
            Category = pdkComp.Category,
            WidthMicrometers = pdkComp.WidthMicrometers,
            HeightMicrometers = pdkComp.HeightMicrometers,
            PinDefinitions = pinDefs,
            NazcaFunctionName = pdkComp.NazcaFunction,
            NazcaParameters = pdkComp.NazcaParameters,
            HasSlider = pdkComp.Sliders?.Any() ?? false,
            SliderMin = pdkComp.Sliders?.FirstOrDefault()?.MinVal ?? 0,
            SliderMax = pdkComp.Sliders?.FirstOrDefault()?.MaxVal ?? 100,
            PdkSource = pdkName,
            NazcaModuleName = nazcaModuleName,
            NazcaOriginOffsetX = nazcaOriginOffsetX,
            NazcaOriginOffsetY = nazcaOriginOffsetY,
        };

        if (pdkComp.SMatrix?.WavelengthData is { Count: > 0 } wlData)
        {
            template.CreateWavelengthSMatrixMap = pins =>
            {
                var map = new Dictionary<int, SMatrix>();
                foreach (var entry in wlData)
                {
                    var draft = new PdkSMatrixDraft
                    {
                        WavelengthNm = entry.WavelengthNm,
                        Connections = entry.Connections
                    };
                    map[entry.WavelengthNm] = CreateSMatrixFromPdk(pins, draft);
                }
                return map;
            };
        }
        else
        {
            template.CreateSMatrix = pins => CreateSMatrixFromPdk(pins, pdkComp.SMatrix);
        }

        return template;
    }

    private static SMatrix CreateSMatrixFromPdk(List<CAP_Core.Components.Core.Pin> pins, PdkSMatrixDraft? sMatrixDraft)
    {
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var sMatrix = new SMatrix(pinIds, new List<(Guid, double)>());

        if (sMatrixDraft?.Connections == null || sMatrixDraft.Connections.Count == 0)
            return sMatrix;

        var pinByName = new Dictionary<string, CAP_Core.Components.Core.Pin>(StringComparer.OrdinalIgnoreCase);
        foreach (var pin in pins)
        {
            pinByName[pin.Name] = pin;
        }

        var transfers = new Dictionary<(Guid, Guid), Complex>();

        foreach (var conn in sMatrixDraft.Connections)
        {
            if (!pinByName.TryGetValue(conn.FromPin, out var fromPin) ||
                !pinByName.TryGetValue(conn.ToPin, out var toPin))
                continue;

            var phaseRad = conn.PhaseDegrees * Math.PI / 180.0;
            var value = Complex.FromPolarCoordinates(conn.Magnitude, phaseRad);

            transfers[(fromPin.IDInFlow, toPin.IDOutFlow)] = value;
            transfers[(toPin.IDInFlow, fromPin.IDOutFlow)] = value;
        }

        sMatrix.SetValues(transfers);
        return sMatrix;
    }
}
