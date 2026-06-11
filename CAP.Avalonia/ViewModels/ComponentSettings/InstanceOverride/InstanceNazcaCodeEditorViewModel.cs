using Avalonia.Media.Imaging;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_DataAccess.Persistence.PIR;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.ComponentSettings.InstanceOverride;

/// <summary>
/// ViewModel for the per-instance editable Nazca code editor (issue #556).
/// Lets a power user see, edit and run a complete Nazca cell function for one
/// placed instance, preview the resulting geometry live, and — on Apply —
/// recompute the component's bounding-box size from the rendered geometry.
/// </summary>
/// <remarks>
/// Geometry-only by design: optical pins and the S-matrix stay as the PDK
/// template. Only <see cref="Component.WidthMicrometers"/> /
/// <see cref="Component.HeightMicrometers"/> change. A mismatch between the
/// rendered port count and the component's pin count surfaces as a hint in
/// <see cref="StatusText"/> and nothing more.
/// </remarks>
public partial class InstanceNazcaCodeEditorViewModel : ObservableObject
{
    private readonly Dictionary<string, NazcaCodeOverride> _storedOverrides;
    private readonly Component? _liveComponent;
    private readonly string _componentKey;
    private readonly NazcaComponentPreviewService? _previewService;
    private readonly Func<double, double, IReadOnlyList<string>>? _overlapCheck;
    private readonly Action? _onDimensionsChanged;
    private readonly Action? _onChanged;
    private readonly string _templateCode;
    private readonly string? _moduleName;
    private readonly string _nazcaFunction;
    private readonly string? _nazcaParameters;

    private NazcaPreviewResult? _lastSuccessfulPreview;

    /// <summary>
    /// The original source the editor was seeded with via module-mode (the component's
    /// real source / note / fallback). Null when the editor was seeded from a stored
    /// raw-code override. Used to decide whether "Run Preview" renders the unchanged
    /// component via module mode (works for demo PDK and SiEPIC PCells alike) or runs
    /// the user's edited code via raw-code mode.
    /// </summary>
    private string? _originalSourceCode;

    /// <summary>The editable raw Nazca cell code for this instance.</summary>
    [ObservableProperty]
    private string _code = string.Empty;

    /// <summary>
    /// True when <see cref="Code"/> has been edited away from the seeded original (or was
    /// seeded from a stored override) — i.e. it is the user's own runnable code, eligible
    /// to be run as raw code and persisted via Apply. False while it is the unchanged
    /// original (which is rendered via module mode and is not itself a persistable override).
    /// </summary>
    private bool IsCustomCode =>
        _originalSourceCode == null
        || !string.Equals((Code ?? string.Empty).Trim(), _originalSourceCode.Trim(), StringComparison.Ordinal);

    /// <summary>Editing the code invalidates the last run and re-evaluates Apply.</summary>
    partial void OnCodeChanged(string value)
    {
        IsValid = false;
        ApplyOverrideCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Error text from the last failed run; empty when the last run succeeded.</summary>
    [ObservableProperty]
    private string _previewError = string.Empty;

    /// <summary>True after a successful run; gates the Apply command.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyOverrideCommand))]
    private bool _isValid;

    /// <summary>True while a preview run is in flight.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyOverrideCommand))]
    private bool _isRunning;

    /// <summary>Free-form status / hint message shown beneath the editor.</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>True when a raw-code override is currently stored for this component.</summary>
    [ObservableProperty]
    private bool _hasOverride;

    /// <summary>
    /// True when the most recent Apply produced a size that overlaps one or more
    /// neighbouring components. Non-blocking — the override is still applied.
    /// </summary>
    [ObservableProperty]
    private bool _hasOverlap;

    /// <summary>
    /// True when this component exposes editable Nazca source (demo PDK cell body
    /// via <c>inspect.getsource</c>, or a SiEPIC PCell Python file). False when the
    /// component is a fixed-cell GDS / KLayout PCell whose source can't be retrieved,
    /// or when source introspection failed. When false the editor still shows the
    /// rendered geometry and the user can paste their own Nazca code to override it.
    /// </summary>
    [ObservableProperty]
    private bool _hasEditableSource = true;

    /// <summary>
    /// The last successful preview's geometry. Null until a run succeeds.
    /// </summary>
    public NazcaPreviewResult? PreviewData => _lastSuccessfulPreview;

    /// <summary>Pixel size used when rasterising the live preview bitmap.</summary>
    private const int PreviewBitmapPixels = 256;

    /// <summary>
    /// Rasterised polygon preview of the last successful run, bound to the editor's
    /// preview Image. Null when there is no successful render (or no polygons —
    /// e.g. gdstk/gdspy not installed; the status text reports that case).
    /// </summary>
    [ObservableProperty]
    private Bitmap? _previewBitmap;

    /// <summary>
    /// Initializes a new instance of <see cref="InstanceNazcaCodeEditorViewModel"/>.
    /// </summary>
    /// <param name="componentKey">The component's Identifier, used as the override-store key.</param>
    /// <param name="storedOverrides">Shared per-instance Nazca override dictionary.</param>
    /// <param name="liveComponent">The live canvas component whose size is recomputed on Apply.</param>
    /// <param name="moduleName">
    /// The component's PDK module name (e.g. "demo", a SiEPIC module). Passed to
    /// <see cref="NazcaComponentPreviewService.RenderAsync"/> in module mode so the
    /// editor can fetch the component's REAL source and render its geometry.
    /// </param>
    /// <param name="nazcaFunction">The component's Nazca function / cell name.</param>
    /// <param name="nazcaParameters">Optional keyword-argument string for the function, or null.</param>
    /// <param name="templateCode">
    /// A runnable code fallback that reproduces the current component's Nazca call.
    /// Used only when module-mode <c>RenderAsync</c> yields no real source AND no
    /// usable note — i.e. as a last-resort seed so the editor is never blank.
    /// </param>
    /// <param name="previewService">Preview back-end. Null disables Run (e.g. headless tests).</param>
    /// <param name="overlapCheck">
    /// Optional callback that, given a candidate width/height, returns the display
    /// names of components the resized instance would overlap. Empty list = no overlap.
    /// </param>
    /// <param name="onDimensionsChanged">Invoked after the live component's size changes so the canvas can repaint.</param>
    /// <param name="onChanged">Invoked after every successful Apply or Reset so observers refresh badges.</param>
    public InstanceNazcaCodeEditorViewModel(
        string componentKey,
        Dictionary<string, NazcaCodeOverride> storedOverrides,
        Component? liveComponent,
        string? moduleName,
        string nazcaFunction,
        string? nazcaParameters,
        string templateCode,
        NazcaComponentPreviewService? previewService = null,
        Func<double, double, IReadOnlyList<string>>? overlapCheck = null,
        Action? onDimensionsChanged = null,
        Action? onChanged = null)
    {
        _componentKey = componentKey;
        _storedOverrides = storedOverrides;
        _liveComponent = liveComponent;
        _moduleName = moduleName;
        _nazcaFunction = nazcaFunction ?? string.Empty;
        _nazcaParameters = nazcaParameters;
        _templateCode = templateCode ?? string.Empty;
        _previewService = previewService;
        _overlapCheck = overlapCheck;
        _onDimensionsChanged = onDimensionsChanged;
        _onChanged = onChanged;

        RefreshFromStore();
    }

    /// <summary>
    /// Loads the component's REAL Nazca source and an initial geometry preview via
    /// module-mode <see cref="NazcaComponentPreviewService.RenderAsync"/>, UNLESS a
    /// raw-code override is already stored (in which case the editor keeps the stored
    /// code seeded by the constructor). Crash-proof: never throws — any failure leaves
    /// the editor usable with an explanatory note.
    /// </summary>
    /// <remarks>
    /// Behaviour when no override is stored:
    /// <list type="bullet">
    /// <item>Real source available → <see cref="Code"/> = source, <see cref="HasEditableSource"/> = true, preview shown.</item>
    /// <item>Only a "# ..." note available (fixed-cell GDS / PCell without Python) →
    ///       <see cref="Code"/> = the note, <see cref="HasEditableSource"/> = false, preview still shown.</item>
    /// <item>Render failed (no python/nazca) → <see cref="Code"/> = a comment, <see cref="HasEditableSource"/> = false,
    ///       <see cref="PreviewError"/> set.</item>
    /// </list>
    /// </remarks>
    public async Task InitializeAsync()
    {
        // A stored raw-code override wins — the constructor already seeded Code from it.
        // Leave _originalSourceCode null so Run treats the stored code as custom (raw-code).
        if (_storedOverrides.TryGetValue(_componentKey, out var stored) && stored.RawCode != null)
            return;

        await LoadOriginalSourceAsync();
        _originalSourceCode = Code;
        ApplyOverrideCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Shared init/Reset logic: fetch the component's original source + geometry via
    /// module-mode render and populate <see cref="Code"/> / <see cref="HasEditableSource"/>
    /// / preview accordingly. Never throws.
    /// </summary>
    private async Task LoadOriginalSourceAsync()
    {
        if (_previewService == null)
        {
            // No back-end (e.g. headless): keep the runnable fallback seed.
            Code = _templateCode;
            HasEditableSource = true;
            return;
        }

        try
        {
            var result = await _previewService.RenderAsync(_moduleName, _nazcaFunction, _nazcaParameters);
            if (!result.Success)
            {
                Code = "# No Nazca source could be loaded for this component.\n" +
                       "# You can still paste your own Nazca code below to override its geometry.\n";
                HasEditableSource = false;
                PreviewBitmap = null;
                _lastSuccessfulPreview = null;
                OnPropertyChanged(nameof(PreviewData));
                PreviewError = result.Error ?? "Could not render this component.";
                StatusText = "No source available — paste your own Nazca code to override.";
                return;
            }

            // Render succeeded — show the geometry regardless of source availability.
            _lastSuccessfulPreview = result;
            OnPropertyChanged(nameof(PreviewData));
            PreviewBitmap = TryRasterize(result);
            PreviewError = string.Empty;

            double w = result.XMax - result.XMin;
            double h = result.YMax - result.YMin;

            if (IsRealSource(result.Source))
            {
                Code = result.Source!;
                HasEditableSource = true;
                StatusText = $"Loaded — size {w:F2} × {h:F2} µm.";
            }
            else
            {
                // No real Python source: show the note (the "# ..." text) if present,
                // else fall back to the runnable template so the editor is never blank.
                Code = !string.IsNullOrWhiteSpace(result.Source) ? result.Source! : _templateCode;
                HasEditableSource = false;
                StatusText = $"Loaded geometry — size {w:F2} × {h:F2} µm. " +
                             "No editable Nazca source for this component.";
            }
        }
        catch (Exception ex)
        {
            // InitializeAsync must never bring the dialog down.
            Code = "# Failed to load Nazca source for this component.\n" +
                   "# You can still paste your own Nazca code below to override its geometry.\n";
            HasEditableSource = false;
            PreviewBitmap = null;
            _lastSuccessfulPreview = null;
            OnPropertyChanged(nameof(PreviewData));
            PreviewError = ex.Message;
            StatusText = "No source available — paste your own Nazca code to override.";
        }
    }

    /// <summary>
    /// True when <paramref name="source"/> is genuine Python source rather than a
    /// note. The preview script returns a "# ..."-prefixed comment (e.g.
    /// "# Source unavailable …") when no source can be retrieved.
    /// </summary>
    private static bool IsRealSource(string? source)
        => !string.IsNullOrWhiteSpace(source) && !source.TrimStart().StartsWith('#');

    /// <summary>
    /// Runs the editor's code through the preview service. Async, non-blocking and
    /// crash-proof: any failure (syntax error, infinite loop → timeout, missing
    /// Python) sets <see cref="PreviewError"/> and leaves <see cref="IsValid"/> false.
    /// Never throws.
    /// </summary>
    [RelayCommand]
    private async Task RunPreviewAsync()
    {
        if (_previewService == null)
        {
            PreviewError = "Preview service unavailable.";
            IsValid = false;
            return;
        }

        IsRunning = true;
        StatusText = "Running preview…";
        try
        {
            // Unedited original → render the real component via module mode (handles demo
            // PDK and SiEPIC PCells, whose source is not standalone-runnable). Edited code
            // → run the user's own self-contained snippet via raw-code mode.
            var result = IsCustomCode
                ? await _previewService.RenderRawCodeAsync(Code)
                : await _previewService.RenderAsync(_moduleName, _nazcaFunction, _nazcaParameters);
            if (result.Success)
            {
                _lastSuccessfulPreview = result;
                OnPropertyChanged(nameof(PreviewData));
                PreviewBitmap = TryRasterize(result);
                PreviewError = string.Empty;
                IsValid = true;
                StatusText = BuildSuccessStatus(result);
            }
            else
            {
                _lastSuccessfulPreview = null;
                OnPropertyChanged(nameof(PreviewData));
                PreviewBitmap = null;
                PreviewError = result.Error ?? "Unknown error.";
                IsValid = false;
                StatusText = "Preview failed — see error above.";
            }
        }
        catch (Exception ex)
        {
            // Defensive: the service is designed never to throw, but a Run command
            // must never bring the dialog down regardless.
            _lastSuccessfulPreview = null;
            OnPropertyChanged(nameof(PreviewData));
            PreviewBitmap = null;
            PreviewError = ex.Message;
            IsValid = false;
            StatusText = "Preview failed — see error above.";
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>
    /// Rasterises the preview polygons to a bitmap, preserving the geometry's aspect
    /// ratio within a fixed pixel budget. Returns null when there are no polygons or
    /// when no UI thread is available (e.g. headless unit tests) — both are non-fatal.
    /// </summary>
    private static Bitmap? TryRasterize(NazcaPreviewResult result)
    {
        if (result.Polygons.Count == 0)
            return null;

        double bboxW = result.XMax - result.XMin;
        double bboxH = result.YMax - result.YMin;
        if (bboxW <= 0 || bboxH <= 0)
            return null;

        // Fit the longer side to the pixel budget so wide/tall cells keep their shape.
        double scale = PreviewBitmapPixels / Math.Max(bboxW, bboxH);
        int w = Math.Max(1, (int)Math.Round(bboxW * scale));
        int h = Math.Max(1, (int)Math.Round(bboxH * scale));

        try
        {
            return GdsPolygonRenderer.RasterizeToBitmap(result, w, h);
        }
        catch
        {
            // RenderTargetBitmap needs a rendering backend; absent in headless tests.
            return null;
        }
    }

    private string BuildSuccessStatus(NazcaPreviewResult result)
    {
        double w = result.XMax - result.XMin;
        double h = result.YMax - result.YMin;
        var status = $"Preview OK — size {w:F2} × {h:F2} µm.";

        // Geometry-only contract: a port-count mismatch is a hint, not a change.
        int templatePinCount = _liveComponent?.PhysicalPins.Count ?? 0;
        if (templatePinCount > 0 && result.Pins.Count != templatePinCount)
            status += $" Note: rendered geometry has {result.Pins.Count} port(s) but this " +
                      $"component keeps its {templatePinCount} pin(s) — connections/sim unchanged.";

        if (!string.IsNullOrEmpty(result.PolygonWarning))
            status += " " + result.PolygonWarning;

        return status;
    }

    /// <summary>
    /// Persists the edited code as a raw-code override, recomputes the live
    /// component's size from the last successful preview's bounding box, runs a
    /// (non-blocking) overlap check, and fires the change callback. Enabled only
    /// after a successful <see cref="RunPreviewAsync"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApplyOverride))]
    private void ApplyOverride()
    {
        if (_lastSuccessfulPreview == null)
            return;

        double width = _lastSuccessfulPreview.XMax - _lastSuccessfulPreview.XMin;
        double height = _lastSuccessfulPreview.YMax - _lastSuccessfulPreview.YMin;

        var overrideData = _storedOverrides.TryGetValue(_componentKey, out var existing)
            ? existing
            : new NazcaCodeOverride();
        overrideData.RawCode = Code;
        overrideData.OverrideWidthMicrometers = width;
        overrideData.OverrideHeightMicrometers = height;
        _storedOverrides[_componentKey] = overrideData;

        if (_liveComponent != null)
        {
            _liveComponent.WidthMicrometers = width;
            _liveComponent.HeightMicrometers = height;
        }
        _onDimensionsChanged?.Invoke();

        var overlapping = _overlapCheck?.Invoke(width, height) ?? Array.Empty<string>();
        HasOverlap = overlapping.Count > 0;
        HasOverride = true;
        StatusText = HasOverlap
            ? $"Applied — geometry resized to {width:F2} × {height:F2} µm. " +
              $"Warning: overlaps {string.Join(", ", overlapping)}."
            : $"Applied — geometry resized to {width:F2} × {height:F2} µm.";

        _onChanged?.Invoke();
    }

    // Apply only persists genuinely custom code — the unchanged original is the PDK
    // default, not an override (and its source may not be standalone-runnable on reload).
    private bool CanApplyOverride() => IsValid && !IsRunning && IsCustomCode;

    /// <summary>
    /// Clears the raw-code override for this instance and restores the editor to the
    /// component's ORIGINAL Nazca source (re-fetched via module-mode render), not a
    /// synthesized template. The live component's size is left as-is (the user can
    /// re-run + re-apply, or reload the design to restore the PDK default size).
    /// Crash-proof — never throws.
    /// </summary>
    [RelayCommand]
    private async Task ResetToTemplate()
    {
        if (_storedOverrides.TryGetValue(_componentKey, out var existing))
        {
            existing.RawCode = null;
            existing.OverrideWidthMicrometers = null;
            existing.OverrideHeightMicrometers = null;
            // Drop the whole record only if no parameter-override fields remain.
            if (existing.FunctionName == null && existing.FunctionParameters == null
                && existing.ModuleName == null)
                _storedOverrides.Remove(_componentKey);
        }

        _lastSuccessfulPreview = null;
        OnPropertyChanged(nameof(PreviewData));
        PreviewBitmap = null;
        PreviewError = string.Empty;
        IsValid = false;
        HasOverlap = false;
        HasOverride = false;

        // Restore the original source + initial preview rather than a stub template.
        await LoadOriginalSourceAsync();
        _originalSourceCode = Code;
        StatusText = "Reset to original source. Run a preview before applying.";
        _onChanged?.Invoke();
    }

    private void RefreshFromStore()
    {
        if (_storedOverrides.TryGetValue(_componentKey, out var stored) && stored.RawCode != null)
        {
            // A stored override is always editable code the user authored/saved.
            Code = stored.RawCode;
            HasOverride = true;
            HasEditableSource = true;
        }
        else
        {
            // Seed with the runnable fallback until InitializeAsync fetches the real source.
            Code = _templateCode;
            HasOverride = false;
        }
    }
}
