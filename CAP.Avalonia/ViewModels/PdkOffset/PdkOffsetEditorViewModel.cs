using System.Collections.ObjectModel;
using Avalonia.Threading;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.PdkOffset;

/// <summary>
/// ViewModel for the PDK Component Offset Editor window.
/// Allows browsing all PDK components, inspecting NazcaOriginOffset status,
/// editing offset values with a live pin-position preview, and saving back to JSON.
/// Optionally displays a Nazca GDS overlay when a preview service is provided.
/// </summary>
public partial class PdkOffsetEditorViewModel : ObservableObject
{
    private readonly PdkLoader _pdkLoader;
    private readonly PdkJsonSaver _pdkSaver;
    private readonly PdkManagerViewModel _pdkManager;
    private readonly NazcaComponentPreviewService? _previewService;
    private PdkDraft? _loadedPdk;
    private string? _loadedFilePath;
    private double _nazcaCanvasRefX;
    private double _nazcaCanvasRefY;
    private CancellationTokenSource? _renderCts;

    [ObservableProperty] private string _statusText = "Load a PDK file to begin.";
    [ObservableProperty] private PdkComponentOffsetItemViewModel? _selectedComponent;
    [ObservableProperty] private double _offsetX;
    [ObservableProperty] private double _offsetY;
    [ObservableProperty] private bool _hasUnsavedChanges;
    [ObservableProperty] private double _componentWidth;
    [ObservableProperty] private double _componentHeight;
    [ObservableProperty] private bool _isNazcaRendering;
    [ObservableProperty] private string _nazcaOverlayStatus = "";
    [ObservableProperty] private bool _hasNazcaOverlay;

    /// <summary>
    /// User toggle for the Nazca GDS overlay. When false the overlay is hidden
    /// even if a successful render is cached — useful when the overlay clutters
    /// the visual or when comparing pin alignment without the polygon noise.
    /// </summary>
    [ObservableProperty] private bool _showNazcaOverlay = true;

    /// <summary>Currently selected PDK from the installed-PDK dropdown; triggers load on change.</summary>
    [ObservableProperty]
    private PdkInfoViewModel? _selectedInstalledPdk;

    /// <summary>All components from the loaded PDK, with offset status badges.</summary>
    public ObservableCollection<PdkComponentOffsetItemViewModel> Components { get; } = new();

    /// <summary>Pin positions for the currently selected component, recalculated on offset change.</summary>
    public ObservableCollection<PinPositionViewModel> PinPositions { get; } = new();

    /// <summary>PDKs currently registered in Lunima, exposed for the installed-PDK dropdown.</summary>
    public ObservableCollection<PdkInfoViewModel> AvailablePdks => _pdkManager.LoadedPdks;

    /// <summary>File dialog service for picking a PDK JSON file to load.</summary>
    public IFileDialogService? FileDialogService { get; set; }

    /// <summary>Component bounding-box width in canvas pixels.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasTotalWidth))]
    private double _canvasComponentWidth;

    /// <summary>Component bounding-box height in canvas pixels.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasTotalHeight))]
    private double _canvasComponentHeight;

    /// <summary>Nazca origin X position in canvas pixels (for the crosshair).</summary>
    [ObservableProperty] private double _canvasOriginX;

    /// <summary>Nazca origin Y position in canvas pixels (for the crosshair).</summary>
    [ObservableProperty] private double _canvasOriginY;

    /// <summary>X offset of the component bounding box inside the canvas.</summary>
    [ObservableProperty] private double _canvasComponentLeft = CanvasPadding;

    /// <summary>Y offset of the component bounding box inside the canvas.</summary>
    [ObservableProperty] private double _canvasComponentTop = CanvasPadding;

    /// <summary>Total canvas width in pixels (component plus both paddings).</summary>
    public double CanvasTotalWidth => CanvasComponentWidth + CanvasPadding * 2;

    /// <summary>Total canvas height in pixels (component plus both paddings).</summary>
    public double CanvasTotalHeight => CanvasComponentHeight + CanvasPadding * 2;

    /// <summary>Pin markers for visual canvas overlay (canvas-pixel coordinates).</summary>
    public ObservableCollection<PinMarker> PinMarkers { get; } = new();

    /// <summary>Nazca GDS polygon markers for the overlay (canvas-pixel coordinates).</summary>
    public ObservableCollection<NazcaPolygonMarker> NazcaPolygons { get; } = new();

    /// <summary>Nazca GDS pin stub markers for the overlay (canvas-pixel coordinates).</summary>
    public ObservableCollection<NazcaStubMarker> NazcaPinStubs { get; } = new();

    private const double CanvasScale = 2.0;
    private const double CanvasPadding = 20.0;

    /// <summary>
    /// Initializes the ViewModel with required services and optional preview service.
    /// </summary>
    /// <summary>
    /// UI-thread marshaller for async render results. Default uses Avalonia's
    /// dispatcher; tests can inject a synchronous executor so the unit-test
    /// runner doesn't have to host an Avalonia application just to wait for
    /// the render to apply.
    /// </summary>
    internal Func<Action, Task> UiThreadMarshaller { get; set; } =
        static async action => await Dispatcher.UIThread.InvokeAsync(action);

    public PdkOffsetEditorViewModel(
        PdkLoader pdkLoader,
        PdkJsonSaver pdkSaver,
        PdkManagerViewModel pdkManager,
        NazcaComponentPreviewService? previewService = null)
    {
        _pdkLoader = pdkLoader;
        _pdkSaver = pdkSaver;
        _pdkManager = pdkManager;
        _previewService = previewService;
    }

    /// <summary>Opens a file dialog and loads the selected PDK JSON file.</summary>
    [RelayCommand]
    private async Task LoadPdkFile()
    {
        if (FileDialogService == null)
        {
            StatusText = "File dialog service not available.";
            return;
        }

        var path = await FileDialogService.ShowOpenFileDialogAsync(
            "Open PDK JSON",
            "JSON Files|*.json|All Files|*.*");

        if (string.IsNullOrEmpty(path)) return;

        try
        {
            LoadPdkFromPath(path);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load PDK: {ex.Message}";
        }
    }

    /// <summary>
    /// Applies the current OffsetX/OffsetY (and optional Width/Height) to the selected
    /// component draft and refreshes the pin position table.
    /// </summary>
    [RelayCommand]
    private void ApplyOffset()
    {
        if (SelectedComponent == null)
        {
            StatusText = "Select a component before applying an offset.";
            return;
        }

        if (!double.IsFinite(OffsetX) || !double.IsFinite(OffsetY))
        {
            StatusText = $"Offset values must be finite numbers (got X={OffsetX}, Y={OffsetY}).";
            return;
        }

        SelectedComponent.Draft.NazcaOriginOffsetX = OffsetX;
        SelectedComponent.Draft.NazcaOriginOffsetY = OffsetY;
        SelectedComponent.Draft.WidthMicrometers = ComponentWidth;
        SelectedComponent.Draft.HeightMicrometers = ComponentHeight;
        SelectedComponent.RefreshStatus();

        RefreshPinPositions(SelectedComponent.Draft);
        RefreshCanvasMarkers(SelectedComponent.Draft);
        HasUnsavedChanges = true;
        StatusText = $"Offset updated for '{SelectedComponent.ComponentName}'. Click Save to persist.";
    }

    /// <summary>Saves the current PDK draft back to its source JSON file.</summary>
    [RelayCommand]
    private void SavePdk()
    {
        if (_loadedPdk == null || string.IsNullOrEmpty(_loadedFilePath))
        {
            StatusText = "No PDK loaded — nothing to save.";
            return;
        }

        try
        {
            _pdkSaver.SaveToFile(_loadedPdk, _loadedFilePath);
            HasUnsavedChanges = false;
            StatusText = $"Saved to {Path.GetFileName(_loadedFilePath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
    }

    partial void OnSelectedInstalledPdkChanged(PdkInfoViewModel? value)
    {
        if (value == null) return;

        if (string.IsNullOrEmpty(value.FilePath))
        {
            StatusText = $"'{value.Name}' has no file path available for editing.";
            return;
        }

        try
        {
            LoadPdkFromPath(value.FilePath);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load PDK: {ex.Message}";
        }
    }

    partial void OnSelectedComponentChanged(PdkComponentOffsetItemViewModel? value)
    {
        if (value == null)
        {
            PinPositions.Clear();
            PinMarkers.Clear();
            NazcaPolygons.Clear();
            NazcaPinStubs.Clear();
            HasNazcaOverlay = false;
            return;
        }

        OffsetX = value.Draft.NazcaOriginOffsetX ?? 0;
        OffsetY = value.Draft.NazcaOriginOffsetY ?? 0;
        ComponentWidth = value.Draft.WidthMicrometers;
        ComponentHeight = value.Draft.HeightMicrometers;

        if (value.Status == OffsetStatus.Missing)
        {
            StatusText = $"'{value.ComponentName}' has no offset in the JSON. Entering 0 and " +
                         "clicking Apply will record it as calibrated-to-zero.";
        }

        // Reset overlay state for new component
        HasNazcaOverlay = false;
        NazcaPolygons.Clear();
        NazcaPinStubs.Clear();
        NazcaOverlayStatus = "";

        RefreshPinPositions(value.Draft);
        RefreshCanvasMarkers(value.Draft);

        if (_previewService != null)
            _ = TriggerNazcaRenderAsync(value.Draft);
    }

    /// <summary>
    /// Loads a PDK from the given file path using the editing-tolerant loader
    /// and populates the component list. Shared by the file-dialog and
    /// installed-PDK-dropdown load paths.
    /// </summary>
    private void LoadPdkFromPath(string path)
    {
        // Use the editing-tolerant loader — this window's whole purpose
        // is to calibrate components whose offsets are still null. The
        // strict LoadFromFile path would reject exactly those PDKs.
        _loadedPdk = _pdkLoader.LoadFromFileForEditing(path);
        _loadedFilePath = path;
        HasUnsavedChanges = false;

        Components.Clear();
        foreach (var comp in _loadedPdk.Components)
            Components.Add(new PdkComponentOffsetItemViewModel(comp, _loadedPdk.Name));

        var missing = Components.Count(c => c.Status == OffsetStatus.Missing);
        var zero   = Components.Count(c => c.Status == OffsetStatus.ZeroOffset);
        StatusText = $"Loaded {_loadedPdk.Name}: {Components.Count} components " +
                     $"({missing} missing offset, {zero} at zero).";
        SelectedComponent = null;
    }

    private void RefreshPinPositions(PdkComponentDraft draft)
    {
        PinPositions.Clear();
        foreach (var pin in draft.Pins)
        {
            PinPositions.Add(new PinPositionViewModel(
                pin.Name,
                pin.OffsetXMicrometers,
                pin.OffsetYMicrometers,
                ComponentHeight,
                OffsetX,
                OffsetY));
        }
    }

    private void RefreshCanvasMarkers(PdkComponentDraft draft)
    {
        CanvasComponentWidth = ComponentWidth * CanvasScale;
        CanvasComponentHeight = ComponentHeight * CanvasScale;

        if (HasNazcaOverlay)
        {
            // Nazca geometry is fixed; move Lunima box as offset changes
            CanvasComponentLeft = _nazcaCanvasRefX - OffsetX * CanvasScale;
            CanvasComponentTop = _nazcaCanvasRefY - (ComponentHeight - OffsetY) * CanvasScale;
            CanvasOriginX = _nazcaCanvasRefX;
            CanvasOriginY = _nazcaCanvasRefY;
        }
        else
        {
            CanvasComponentLeft = CanvasPadding;
            CanvasComponentTop = CanvasPadding;
            CanvasOriginX = CanvasPadding + OffsetX * CanvasScale;
            CanvasOriginY = CanvasPadding + (ComponentHeight - OffsetY) * CanvasScale;
        }

        PinMarkers.Clear();
        foreach (var pin in draft.Pins)
        {
            PinMarkers.Add(new PinMarker(
                pin.Name,
                CanvasComponentLeft + pin.OffsetXMicrometers * CanvasScale,
                CanvasComponentTop + pin.OffsetYMicrometers * CanvasScale));
        }
    }

    /// <summary>
    /// Applies a Nazca preview result, transforming coordinates to canvas space
    /// and populating the overlay collections.
    /// </summary>
    private void SetNazcaOverlay(NazcaPreviewResult result)
    {
        // Nazca origin is at (0,0) in Nazca space; map to canvas
        _nazcaCanvasRefX = CanvasPadding + (-result.XMin) * CanvasScale;
        _nazcaCanvasRefY = CanvasPadding + result.YMax * CanvasScale;

        NazcaPolygons.Clear();
        foreach (var poly in result.Polygons)
        {
            var canvasPts = poly.Vertices
                .Select(v => (
                    X: _nazcaCanvasRefX + v.X * CanvasScale,
                    Y: _nazcaCanvasRefY - v.Y * CanvasScale))
                .ToList();
            NazcaPolygons.Add(new NazcaPolygonMarker(poly.Layer, canvasPts));
        }

        NazcaPinStubs.Clear();
        foreach (var pin in result.Pins)
        {
            var x0 = _nazcaCanvasRefX + pin.X * CanvasScale;
            var y0 = _nazcaCanvasRefY - pin.Y * CanvasScale;
            var x1 = _nazcaCanvasRefX + pin.StubX1 * CanvasScale;
            var y1 = _nazcaCanvasRefY - pin.StubY1 * CanvasScale;
            NazcaPinStubs.Add(new NazcaStubMarker(pin.Name, x0, y0, x1, y1));
        }

        HasNazcaOverlay = true;
        if (SelectedComponent != null)
            RefreshCanvasMarkers(SelectedComponent.Draft);
    }

    /// <summary>Triggers an async Nazca render for the given component draft.</summary>
    private async Task TriggerNazcaRenderAsync(PdkComponentDraft draft)
    {
        _renderCts?.Cancel();
        _renderCts = new CancellationTokenSource();
        var token = _renderCts.Token;

        // Capture the draft this render was started for so a fast user-click that
        // changes SelectedComponent mid-flight cannot stamp our overlay on top of
        // a newer component's offsets.
        var draftAtStart = draft;

        IsNazcaRendering = true;
        NazcaOverlayStatus = "Rendering Nazca GDS preview…";

        try
        {
            var (module, function) = ResolveModuleAndFunction(draft.NazcaFunction);
            var result = await _previewService!.RenderAsync(
                module,
                function,
                draft.NazcaParameters,
                token);

            if (token.IsCancellationRequested) return;
            // SelectedComponent has moved on while we were waiting — drop result.
            if (SelectedComponent?.Draft != draftAtStart) return;

            // RenderAsync returns on a thread-pool thread; ObservableCollection
            // mutations downstream must happen on the UI thread.
            await UiThreadMarshaller(() =>
            {
                if (token.IsCancellationRequested) return;
                if (SelectedComponent?.Draft != draftAtStart) return;

                if (result.Success)
                {
                    SetNazcaOverlay(result);
                    NazcaOverlayStatus = $"GDS overlay loaded ({result.Polygons.Count} polygons, {result.Pins.Count} pins).";
                }
                else
                {
                    HasNazcaOverlay = false;
                    NazcaOverlayStatus = $"Preview unavailable: {result.Error}";
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection — no status update needed
        }
        catch (Exception ex)
        {
            await UiThreadMarshaller(() =>
            {
                HasNazcaOverlay = false;
                NazcaOverlayStatus = $"Preview error: {ex.Message}";
            });
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsNazcaRendering = false;
        }
    }

    /// <summary>
    /// Splits a NazcaFunction string into a Python module name and a bare
    /// function name. Two cases the PDKs use today:
    /// <list type="bullet">
    ///   <item><c>"demo.mmi2x2_dp"</c> — demofab uses dotted notation; split
    ///     at the last dot.</item>
    ///   <item><c>"ebeam_y_1550"</c> — SiEPIC EBeam exposes flat names; the
    ///     name prefix tells us which Python module owns it.</item>
    /// </list>
    /// Exposed as internal so the unit tests can lock the mapping in directly
    /// rather than going through the full async render pipeline.
    /// </summary>
    internal static (string module, string function) ResolveModuleAndFunction(string? nazcaFunction)
    {
        if (string.IsNullOrWhiteSpace(nazcaFunction))
            return ("demo", "");

        var lastDot = nazcaFunction.LastIndexOf('.');
        if (lastDot > 0)
        {
            var prefix = nazcaFunction[..lastDot];
            var fn = nazcaFunction[(lastDot + 1)..];
            // Both 'demo.foo' and 'demo_pdk.foo' (the latter appears in some
            // Lunima PDK JSONs) resolve to nazca.demofab — let the script see
            // the canonical 'demo' so it doesn't try to importlib 'demo_pdk'.
            if (prefix == "demo_pdk") prefix = "demo";
            return (prefix, fn);
        }

        // SiEPIC EBeam PDK ships flat names — these prefixes are the existing
        // convention used elsewhere in the repo (see SimpleNazcaExporter.IsPdkFunction).
        if (nazcaFunction.StartsWith("ebeam_", StringComparison.Ordinal) ||
            nazcaFunction.StartsWith("gc_",    StringComparison.Ordinal) ||
            nazcaFunction.StartsWith("ANT_",   StringComparison.Ordinal) ||
            nazcaFunction.StartsWith("crossing_", StringComparison.Ordinal) ||
            nazcaFunction.StartsWith("taper_", StringComparison.Ordinal))
        {
            return ("siepic_ebeam_pdk", nazcaFunction);
        }

        // Anything else: assume demofab, the bundled Nazca PDK.
        return ("demo", nazcaFunction);
    }
}
