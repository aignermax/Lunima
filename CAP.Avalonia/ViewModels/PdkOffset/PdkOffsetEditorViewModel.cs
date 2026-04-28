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
    // Right / bottom extent of the rendered Nazca geometry in canvas pixels.
    // Used so CanvasTotalWidth/Height also covers SiEPIC PCells whose actual
    // bbox is wider than the Lunima JSON's WidthMicrometers/HeightMicrometers.
    private double _nazcaCanvasRight;
    private double _nazcaCanvasBottom;
    private CancellationTokenSource? _renderCts;
    // Cached so the Auto-Calibrate command can read the bbox + pin positions
    // of the most recent successful render without rerunning the Python helper.
    private NazcaPreviewResult? _lastNazcaResult;

    [ObservableProperty] private string _statusText = "Load a PDK file to begin.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AutoCalibrateCommand))]
    private PdkComponentOffsetItemViewModel? _selectedComponent;

    [ObservableProperty] private double _offsetX;
    [ObservableProperty] private double _offsetY;
    [ObservableProperty] private bool _hasUnsavedChanges;
    [ObservableProperty] private double _componentWidth;
    [ObservableProperty] private double _componentHeight;
    [ObservableProperty] private bool _isNazcaRendering;
    [ObservableProperty] private string _nazcaOverlayStatus = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AutoCalibrateCommand))]
    private bool _hasNazcaOverlay;

    /// <summary>
    /// User toggle for the Nazca GDS overlay. When false the overlay is hidden
    /// even if a successful render is cached — useful when the overlay clutters
    /// the visual or when comparing pin alignment without the polygon noise.
    /// </summary>
    [ObservableProperty] private bool _showNazcaOverlay = true;

    /// <summary>
    /// One-line summary of the pin-alignment check ("3/4 pins aligned" etc.)
    /// computed against the cached Nazca render. Empty until the first render.
    /// </summary>
    [ObservableProperty] private string _pinAlignmentSummary = "";

    /// <summary>
    /// Tolerance in micrometres below which a Lunima pin is considered to be
    /// aligned with its nearest Nazca pin. 0.5 µm is generous enough to ignore
    /// sub-grid rounding noise without masking real offset errors.
    /// </summary>
    public const double PinAlignmentToleranceMicrometers = 0.5;

    /// <summary>Per-pin alignment detail. Populated by <see cref="ComputePinAlignment"/>.</summary>
    public ObservableCollection<PinAlignmentInfo> PinAlignmentResults { get; } = new();

    /// <summary>
    /// Source snippet for the currently selected component — what the preview
    /// would tell Python to render. Empty until a component is selected.
    /// Populated by <see cref="OnSelectedComponentChanged"/>.
    /// </summary>
    [ObservableProperty] private string _previewSource = "";

    /// <summary>
    /// Callback for copying text to the OS clipboard. Wired by the window's
    /// code-behind via <c>TopLevel.GetTopLevel(this).Clipboard</c>.
    /// </summary>
    public Func<string, Task>? CopyToClipboard { get; set; }

    /// <summary>Copies the Nazca overlay status text to the clipboard.</summary>
    [RelayCommand]
    private async Task CopyStatus()
    {
        if (CopyToClipboard == null || string.IsNullOrEmpty(NazcaOverlayStatus)) return;
        await CopyToClipboard(NazcaOverlayStatus);
    }

    /// <summary>
    /// Compares Lunima's PDK-JSON pin positions against the Nazca render's
    /// pin stubs (in Nazca-space micrometres) and populates
    /// <see cref="PinAlignmentResults"/> + <see cref="PinAlignmentSummary"/>.
    /// Each Lunima pin is matched to its nearest Nazca pin by Euclidean
    /// distance — name-matching is unreliable across PDKs (Lunima uses
    /// "in"/"out", SiEPIC uses "opt1"/"opt2").
    /// </summary>
    internal void ComputePinAlignment(NazcaPreviewResult result, PdkComponentDraft draft)
    {
        PinAlignmentResults.Clear();
        if (result.Pins.Count == 0 || draft.Pins.Count == 0)
        {
            PinAlignmentSummary = result.Pins.Count == 0
                ? "Nazca cell exposes no pins — Lunima pin positions cannot be cross-checked."
                : "Lunima component has no pins defined.";
            return;
        }

        int aligned = 0;
        foreach (var lp in draft.Pins)
        {
            // Lunima pin position in Nazca-space micrometres. Lunima offsets
            // are measured from the bbox top-left in y-down. The Nazca origin
            // sits at (NazcaOriginOffsetX, ComponentHeight - NazcaOriginOffsetY)
            // inside that bbox in y-down — i.e. the offset Y is measured from
            // the bottom edge upward, the Lunima pin Y from the top edge down.
            // The y-flip therefore needs the ComponentHeight term to subtract
            // the Lunima distance from the bottom, then push to Nazca origin.
            // Same formula as PinPositionViewModel.NazcaRelY.
            var lunimaNazcaX = lp.OffsetXMicrometers - (draft.NazcaOriginOffsetX ?? 0);
            var lunimaNazcaY = (draft.HeightMicrometers - lp.OffsetYMicrometers)
                               - (draft.NazcaOriginOffsetY ?? 0);

            var nearest = result.Pins
                .Select(np => (np, dist: Math.Sqrt(
                    (np.X - lunimaNazcaX) * (np.X - lunimaNazcaX) +
                    (np.Y - lunimaNazcaY) * (np.Y - lunimaNazcaY))))
                .OrderBy(t => t.dist)
                .First();

            var dx = nearest.np.X - lunimaNazcaX;
            var dy = nearest.np.Y - lunimaNazcaY;
            var isAligned = nearest.dist <= PinAlignmentToleranceMicrometers;
            if (isAligned) aligned++;

            PinAlignmentResults.Add(new PinAlignmentInfo(
                lp.Name, nearest.np.Name, dx, dy, nearest.dist, isAligned));
        }

        PinAlignmentSummary = aligned == draft.Pins.Count
            ? $"✓ All {aligned}/{draft.Pins.Count} Lunima pins align with Nazca pins (≤{PinAlignmentToleranceMicrometers:F1} µm)."
            : $"⚠ {aligned}/{draft.Pins.Count} pins aligned. Worst delta: " +
              $"{PinAlignmentResults.Max(p => p.DistanceMicrometers):F2} µm — adjust NazcaOriginOffset.";
    }

    /// <summary>
    /// Derives Width / Height / NazcaOriginOffset from the cached Nazca bbox
    /// and snaps every Lunima pin to its matched Nazca pin position. The user
    /// no longer has to reverse-engineer the bbox math — one click and the
    /// JSON aligns with the GDS down to the pin.
    ///
    /// Pin matching is greedy bipartite by Euclidean distance using the
    /// component's CURRENT calibration as the projection space, so a
    /// roughly-correct starting offset is enough. Pin counts must match —
    /// otherwise the command refuses with an explicit error so the user
    /// knows the mismatch is real (e.g. SiEPIC GC has 'io' + 'wg' but the
    /// Lunima JSON only declares one pin).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAutoCalibrate))]
    private void AutoCalibrate()
    {
        if (_lastNazcaResult is not { Success: true } r || SelectedComponent == null)
        {
            StatusText = "Auto-calibrate needs a successful Nazca preview.";
            return;
        }

        var draft = SelectedComponent.Draft;

        if (r.XMax <= r.XMin || r.YMax <= r.YMin)
        {
            StatusText = "Auto-calibrate aborted: Nazca bbox is degenerate " +
                         $"(XMin={r.XMin}, XMax={r.XMax}, YMin={r.YMin}, YMax={r.YMax}).";
            return;
        }

        if (r.Pins.Count != draft.Pins.Count)
        {
            StatusText = $"Auto-calibrate aborted: Lunima component '{SelectedComponent.ComponentName}' " +
                         $"declares {draft.Pins.Count} pins but the Nazca cell exposes {r.Pins.Count} — " +
                         "pin counts must match for unambiguous alignment.";
            return;
        }

        var pairs = MatchPinsByGreedyNearest(draft, r);

        draft.WidthMicrometers = r.XMax - r.XMin;
        draft.HeightMicrometers = r.YMax - r.YMin;
        draft.NazcaOriginOffsetX = -r.XMin;
        draft.NazcaOriginOffsetY = -r.YMin;

        foreach (var (lp, np) in pairs)
        {
            lp.OffsetXMicrometers = np.X - r.XMin;
            lp.OffsetYMicrometers = r.YMax - np.Y;
        }

        // Mirror back into the bound numeric controls so the editor reflects
        // the new calibration without requiring the user to re-select the row.
        OffsetX = draft.NazcaOriginOffsetX.Value;
        OffsetY = draft.NazcaOriginOffsetY.Value;
        ComponentWidth = draft.WidthMicrometers;
        ComponentHeight = draft.HeightMicrometers;

        SelectedComponent.RefreshStatus();
        RefreshPinPositions(draft);
        RefreshCanvasMarkers(draft);
        ComputePinAlignment(r, draft);
        HasUnsavedChanges = true;
        StatusText = $"Auto-calibrated '{SelectedComponent.ComponentName}' from GDS bbox " +
                     $"({draft.WidthMicrometers:F2} × {draft.HeightMicrometers:F2} µm, " +
                     $"origin {draft.NazcaOriginOffsetX:F2}/{draft.NazcaOriginOffsetY:F2}). " +
                     "Click Save to persist.";
    }

    private bool CanAutoCalibrate() =>
        _lastNazcaResult is { Success: true } && SelectedComponent != null;

    /// <summary>
    /// Test seam: lets unit tests place a synthetic <see cref="NazcaPreviewResult"/>
    /// into the cache slot the AutoCalibrate command reads from, without spinning
    /// up the Python preview pipeline.
    /// </summary>
    internal void SeedNazcaResultForTesting(NazcaPreviewResult result)
    {
        _lastNazcaResult = result;
        AutoCalibrateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Greedy bipartite pin matcher: repeatedly takes the closest Lunima/Nazca
    /// pair (in current Lunima→Nazca-space projection) and removes both from
    /// the candidate sets. Returns one pair per Lunima pin assuming counts
    /// match. Exposed as internal so the unit tests can pin the assignment.
    /// </summary>
    internal static List<(PhysicalPinDraft Lunima, NazcaPreviewPin Nazca)>
        MatchPinsByGreedyNearest(PdkComponentDraft draft, NazcaPreviewResult result)
    {
        var pairs = new List<(PhysicalPinDraft, NazcaPreviewPin)>();
        // Project Lunima pins to Nazca-space with the CURRENT calibration so a
        // roughly-correct starting offset still matches pins correctly even if
        // the box dimensions are off.
        var projections = draft.Pins
            .Select(lp => (
                lp,
                x: lp.OffsetXMicrometers - (draft.NazcaOriginOffsetX ?? 0),
                y: (draft.HeightMicrometers - lp.OffsetYMicrometers) - (draft.NazcaOriginOffsetY ?? 0)))
            .ToList();
        var availableNazca = result.Pins.ToList();

        while (projections.Count > 0 && availableNazca.Count > 0)
        {
            var best = (lpIdx: -1, npIdx: -1, dist: double.MaxValue);
            for (var i = 0; i < projections.Count; i++)
            {
                for (var j = 0; j < availableNazca.Count; j++)
                {
                    var dx = availableNazca[j].X - projections[i].x;
                    var dy = availableNazca[j].Y - projections[i].y;
                    var d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < best.dist) best = (i, j, d);
                }
            }
            pairs.Add((projections[best.lpIdx].lp, availableNazca[best.npIdx]));
            projections.RemoveAt(best.lpIdx);
            availableNazca.RemoveAt(best.npIdx);
        }
        return pairs;
    }

    /// <summary>Window-side hook that resets the canvas zoom slider to 1.0.</summary>
    public Action? ResetZoomHook { get; set; }

    /// <summary>Resets the overlay zoom to 1:1 via the window's slider.</summary>
    [RelayCommand]
    private void ResetZoom() => ResetZoomHook?.Invoke();

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

    /// <summary>
    /// Total canvas width in pixels — sized to fit both the Lunima box and
    /// any Nazca geometry that extends past it. The user pans the
    /// surrounding ScrollViewer to bring off-screen geometry into view; the
    /// canvas itself stays at its natural size so the offset/alignment
    /// visualisation is geometrically meaningful and doesn't squish.
    /// </summary>
    public double CanvasTotalWidth =>
        Math.Max(CanvasComponentWidth + CanvasPadding * 2, _nazcaCanvasRight + CanvasPadding);

    /// <inheritdoc cref="CanvasTotalWidth"/>
    public double CanvasTotalHeight =>
        Math.Max(CanvasComponentHeight + CanvasPadding * 2, _nazcaCanvasBottom + CanvasPadding);

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
        _lastNazcaResult = null;
        NazcaPolygons.Clear();
        NazcaPinStubs.Clear();
        NazcaOverlayStatus = "";
        PinAlignmentSummary = "";
        PinAlignmentResults.Clear();
        _nazcaCanvasRight = 0;
        _nazcaCanvasBottom = 0;
        OnPropertyChanged(nameof(CanvasTotalWidth));
        OnPropertyChanged(nameof(CanvasTotalHeight));
        PreviewSource = BuildPreviewSource(value.Draft);

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
        // Track the Nazca bbox right/bottom so the total canvas size grows to
        // fit polygons that extend past the Lunima JSON's WidthMicrometers.
        _nazcaCanvasRight = _nazcaCanvasRefX + result.XMax * CanvasScale;
        _nazcaCanvasBottom = _nazcaCanvasRefY - result.YMin * CanvasScale;
        OnPropertyChanged(nameof(CanvasTotalWidth));
        OnPropertyChanged(nameof(CanvasTotalHeight));

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
                    _lastNazcaResult = result;
                    SetNazcaOverlay(result);
                    var status = $"GDS overlay loaded ({result.Polygons.Count} polygons, {result.Pins.Count} pins).";
                    if (!string.IsNullOrEmpty(result.PolygonWarning))
                        status += "  " + result.PolygonWarning;
                    NazcaOverlayStatus = status;
                    // Replace the synthetic Lunima-side snippet with the actual
                    // PDK function source pulled live by the helper script.
                    if (!string.IsNullOrEmpty(result.Source))
                        PreviewSource = result.Source;
                    ComputePinAlignment(result, draftAtStart);
                }
                else
                {
                    _lastNazcaResult = null;
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
    /// Renders the same Python the preview helper would execute, as a string
    /// the user can read and copy. Different shape per render path:
    /// SiEPIC → klayout GDS load; demofab → Nazca cell call.
    /// </summary>
    private static string BuildPreviewSource(PdkComponentDraft draft)
    {
        var (module, function) = ResolveModuleAndFunction(draft.NazcaFunction);
        var paramsBlock = string.IsNullOrWhiteSpace(draft.NazcaParameters)
            ? "" : draft.NazcaParameters;

        if (module.StartsWith("siepic", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join("\n",
                "# SiEPIC components are read from their bundled fixed-cell GDS:",
                $"#   <{module}-package>/gds/EBeam/{function}.gds",
                "",
                "import os, klayout.db as kdb",
                $"import {module}",
                $"pkg_dir = os.path.dirname({module}.__file__)",
                $"gds = os.path.join(pkg_dir, 'gds', 'EBeam', '{function}.gds')",
                "ly = kdb.Layout(); ly.read(gds)",
                "cell = next(ly.each_cell())",
                "# polygons on layer 1/0, pins on layer 1/10");
        }

        var moduleImport = module == "demo" ? "import nazca.demofab as demo" : $"import {module} as mod";
        var modAlias = module == "demo" ? "demo" : "mod";
        var paramsRepr = string.IsNullOrEmpty(paramsBlock) ? "" : paramsBlock;
        return string.Join("\n",
            "# The preview helper builds the cell, exports a temp GDS,",
            "# and reads back polygons + pins via klayout/gdstk.",
            "import nazca as nd",
            moduleImport,
            $"cell = {modAlias}.{function}({paramsRepr})",
            "nd.export_gds(topcells=[cell], filename='preview.gds')");
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

/// <summary>
/// Per-pin alignment record produced by
/// <see cref="PdkOffsetEditorViewModel.ComputePinAlignment"/>.
/// </summary>
public record PinAlignmentInfo(
    string LunimaPinName,
    string NearestNazcaPinName,
    double DeltaXMicrometers,
    double DeltaYMicrometers,
    double DistanceMicrometers,
    bool IsAligned)
{
    /// <summary>Display-only label — '✓ aligned' or '✗ off' for the per-pin row.</summary>
    public string AlignedLabel => IsAligned ? "✓ aligned" : "✗ off";
}
