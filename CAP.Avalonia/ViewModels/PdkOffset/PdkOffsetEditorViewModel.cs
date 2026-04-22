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

    /// <summary>
    /// Canvas geometry properties for the visual pin overlay.
    /// These are updated when a component is selected or offset changes.
    /// </summary>
    public double CanvasComponentWidth { get; private set; }
    public double CanvasComponentHeight { get; private set; }
    public double CanvasOriginX { get; private set; }
    public double CanvasOriginY { get; private set; }

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
            _loadedPdk = _pdkLoader.LoadFromFile(path);
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
    /// and refreshes the pin position table.
    /// </summary>
    [RelayCommand]
    private void ApplyOffset()
    {
        if (SelectedComponent == null) return;

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

        OnPropertyChanged(nameof(CanvasComponentWidth));
        OnPropertyChanged(nameof(CanvasComponentHeight));
        OnPropertyChanged(nameof(CanvasOriginX));
        OnPropertyChanged(nameof(CanvasOriginY));
    }
}

/// <summary>
/// Position of a pin marker on the visual canvas overlay.
/// </summary>
/// <param name="Name">Pin name for the tooltip label.</param>
/// <param name="CanvasX">X coordinate in canvas pixels.</param>
/// <param name="CanvasY">Y coordinate in canvas pixels.</param>
public record PinMarker(string Name, double CanvasX, double CanvasY);
