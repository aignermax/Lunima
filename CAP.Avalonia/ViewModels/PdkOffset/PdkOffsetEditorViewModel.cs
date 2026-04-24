using System.Collections.ObjectModel;
using CAP.Avalonia.Services;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.PdkOffset;

/// <summary>
/// ViewModel for the PDK Component Offset Editor window.
/// Allows browsing all PDK components, inspecting NazcaOriginOffset status,
/// editing offset values with a live pin-position preview, and saving back to JSON.
/// </summary>
public partial class PdkOffsetEditorViewModel : ObservableObject
{
    private readonly PdkLoader _pdkLoader;
    private readonly PdkJsonSaver _pdkSaver;
    private PdkDraft? _loadedPdk;
    private string? _loadedFilePath;

    [ObservableProperty]
    private string _statusText = "Load a PDK file to begin.";

    [ObservableProperty]
    private PdkComponentOffsetItemViewModel? _selectedComponent;

    [ObservableProperty]
    private double _offsetX;

    [ObservableProperty]
    private double _offsetY;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    /// <summary>All components from the loaded PDK, with offset status badges.</summary>
    public ObservableCollection<PdkComponentOffsetItemViewModel> Components { get; } = new();

    /// <summary>Pin positions for the currently selected component, recalculated on offset change.</summary>
    public ObservableCollection<PinPositionViewModel> PinPositions { get; } = new();

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
    [ObservableProperty]
    private double _canvasOriginX;

    /// <summary>Nazca origin Y position in canvas pixels (for the crosshair).</summary>
    [ObservableProperty]
    private double _canvasOriginY;

    /// <summary>Total canvas width in pixels (component plus both paddings).</summary>
    public double CanvasTotalWidth => CanvasComponentWidth + CanvasPadding * 2;

    /// <summary>Total canvas height in pixels (component plus both paddings).</summary>
    public double CanvasTotalHeight => CanvasComponentHeight + CanvasPadding * 2;

    /// <summary>X offset of the component bounding box inside the canvas (= padding).</summary>
    public double CanvasComponentLeft => CanvasPadding;

    /// <summary>Y offset of the component bounding box inside the canvas (= padding).</summary>
    public double CanvasComponentTop => CanvasPadding;

    /// <summary>Pin markers for visual canvas overlay (canvas-pixel coordinates).</summary>
    public ObservableCollection<PinMarker> PinMarkers { get; } = new();

    private const double CanvasScale = 2.0;    // pixels per µm
    private const double CanvasPadding = 20.0;

    /// <summary>
    /// Initializes the ViewModel with required services.
    /// </summary>
    public PdkOffsetEditorViewModel(PdkLoader pdkLoader, PdkJsonSaver pdkSaver)
    {
        _pdkLoader = pdkLoader;
        _pdkSaver = pdkSaver;
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
            // Use the editing-tolerant loader — this window's whole purpose
            // is to calibrate components whose offsets are still null. The
            // strict LoadFromFile path would reject exactly those PDKs.
            _loadedPdk = _pdkLoader.LoadFromFileForEditing(path);
            _loadedFilePath = path;
            HasUnsavedChanges = false;

            Components.Clear();
            foreach (var comp in _loadedPdk.Components)
            {
                Components.Add(new PdkComponentOffsetItemViewModel(comp, _loadedPdk.Name));
            }

            var missing = Components.Count(c => c.Status == OffsetStatus.Missing);
            var zero   = Components.Count(c => c.Status == OffsetStatus.ZeroOffset);
            StatusText = $"Loaded {_loadedPdk.Name}: {Components.Count} components " +
                         $"({missing} missing offset, {zero} at zero).";
            SelectedComponent = null;
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load PDK: {ex.Message}";
        }
    }

    /// <summary>
    /// Applies the current OffsetX/OffsetY to the selected component draft
    /// and refreshes the pin position table. Rejects non-finite values
    /// (NaN / ±Infinity) — they would silently propagate into the JSON and
    /// later break GDS export with cryptic coordinate errors.
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
        SelectedComponent.RefreshStatus();

        RefreshPinPositions(SelectedComponent.Draft);
        RefreshCanvasMarkers(SelectedComponent.Draft);
        HasUnsavedChanges = true;
        StatusText = $"Offset updated for '{SelectedComponent.ComponentName}'. Click Save to persist.";
    }

    /// <summary>
    /// Saves the current PDK draft (with all edited offsets) back to its source JSON file.
    /// </summary>
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

    partial void OnSelectedComponentChanged(PdkComponentOffsetItemViewModel? value)
    {
        if (value == null)
        {
            PinPositions.Clear();
            PinMarkers.Clear();
            return;
        }

        OffsetX = value.Draft.NazcaOriginOffsetX ?? 0;
        OffsetY = value.Draft.NazcaOriginOffsetY ?? 0;

        // Warn when the selected component has no offset in the JSON. The edit
        // fields show 0/0 as a display default, but hitting Apply without
        // realizing would flip the JSON from "null" (missing) to "0.0" (set
        // to zero) — the file would then claim the component has been
        // calibrated when it hasn't. Make the state visible.
        if (value.Status == OffsetStatus.Missing)
        {
            StatusText = $"'{value.ComponentName}' has no offset in the JSON. Entering 0 and " +
                         "clicking Apply will record it as calibrated-to-zero.";
        }

        RefreshPinPositions(value.Draft);
        RefreshCanvasMarkers(value.Draft);
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
                draft.HeightMicrometers,
                OffsetX,
                OffsetY));
        }
    }

    private void RefreshCanvasMarkers(PdkComponentDraft draft)
    {
        CanvasComponentWidth  = draft.WidthMicrometers  * CanvasScale;
        CanvasComponentHeight = draft.HeightMicrometers * CanvasScale;
        CanvasOriginX = CanvasPadding + OffsetX  * CanvasScale;
        CanvasOriginY = CanvasPadding + (draft.HeightMicrometers - OffsetY) * CanvasScale;

        PinMarkers.Clear();
        foreach (var pin in draft.Pins)
        {
            PinMarkers.Add(new PinMarker(
                pin.Name,
                CanvasPadding + pin.OffsetXMicrometers * CanvasScale,
                CanvasPadding + pin.OffsetYMicrometers * CanvasScale));
        }
    }
}
