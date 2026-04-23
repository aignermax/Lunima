using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core;
using CAP_Core.Grid;
using CAP.Avalonia.Services;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// ViewModel for the Chip Size settings panel.
/// Exposes chip dimensions in millimeters (user-facing) and derives tile counts.
/// Applies the new footprint to the canvas immediately on <see cref="ApplyCommand"/>.
/// The previous user default is persisted in <see cref="UserPreferencesService"/>.
/// </summary>
public partial class ChipSizeViewModel : ObservableObject
{
    private readonly UserPreferencesService _preferences;
    private DesignCanvasViewModel? _canvas;

    // ── Observable fields (millimeters, user-facing) ─────────────────────
    [ObservableProperty] private double _widthMillimeters;
    [ObservableProperty] private double _heightMillimeters;
    [ObservableProperty] private ChipSizePreset? _selectedPreset;
    [ObservableProperty] private string _statusText = "";

    // ── Derived (read-only) ───────────────────────────────────────────────
    /// <summary>Number of tile columns at 250 μm pitch.</summary>
    public int TileColumns
        => (int)(_widthMillimeters * 1000.0 / PhotonicConstants.GridSizeMicrometers);

    /// <summary>Number of tile rows at 250 μm pitch.</summary>
    public int TileRows
        => (int)(_heightMillimeters * 1000.0 / PhotonicConstants.GridSizeMicrometers);

    /// <summary>Available MPW / shuttle-mask presets plus a Custom entry.</summary>
    public ObservableCollection<ChipSizePreset> Presets { get; }
        = new(ChipSizeConfiguration.Presets);

    // ── Constructor ───────────────────────────────────────────────────────

    /// <summary>Initializes the ViewModel from persisted user defaults.</summary>
    public ChipSizeViewModel(UserPreferencesService preferences)
    {
        _preferences = preferences;

        _widthMillimeters  = preferences.GetDefaultChipWidthMm();
        _heightMillimeters = preferences.GetDefaultChipHeightMm();
        _selectedPreset    = FindMatchingPreset(_widthMillimeters, _heightMillimeters);
    }

    /// <summary>
    /// Wires the ViewModel to the canvas and applies the initial chip size.
    /// Called from <see cref="RightPanelViewModel"/> after DI construction.
    /// </summary>
    public void Configure(DesignCanvasViewModel canvas)
    {
        _canvas = canvas;
        ApplyToCanvas(_widthMillimeters * 1000.0, _heightMillimeters * 1000.0);
    }

    // ── Property change handlers ──────────────────────────────────────────

    partial void OnWidthMillimetersChanged(double value)
    {
        OnPropertyChanged(nameof(TileColumns));
        _selectedPreset = FindMatchingPreset(value, _heightMillimeters);
        OnPropertyChanged(nameof(SelectedPreset));
    }

    partial void OnHeightMillimetersChanged(double value)
    {
        OnPropertyChanged(nameof(TileRows));
        _selectedPreset = FindMatchingPreset(_widthMillimeters, value);
        OnPropertyChanged(nameof(SelectedPreset));
    }

    partial void OnSelectedPresetChanged(ChipSizePreset? value)
    {
        if (value is null || value.IsCustom) return;
        _widthMillimeters  = value.WidthMm;
        _heightMillimeters = value.HeightMm;
        OnPropertyChanged(nameof(WidthMillimeters));
        OnPropertyChanged(nameof(HeightMillimeters));
        OnPropertyChanged(nameof(TileColumns));
        OnPropertyChanged(nameof(TileRows));
    }

    // ── Commands ──────────────────────────────────────────────────────────

    /// <summary>
    /// Applies the current width × height to the canvas, saves the user default,
    /// and refreshes the out-of-bounds flag on all placed components.
    /// </summary>
    [RelayCommand]
    private void Apply()
    {
        if (_widthMillimeters <= 0 || _heightMillimeters <= 0)
        {
            StatusText = "Width and height must be positive.";
            return;
        }

        double widthUm  = _widthMillimeters  * 1000.0;
        double heightUm = _heightMillimeters * 1000.0;

        ApplyToCanvas(widthUm, heightUm);
        _preferences.SetDefaultChipSize(_widthMillimeters, _heightMillimeters);

        int outOfBounds = _canvas?.Components
            .Count(c => c.IsOutOfBounds) ?? 0;

        StatusText = outOfBounds == 0
            ? $"Chip size applied: {TileColumns} × {TileRows} tiles"
            : $"Chip size applied — {outOfBounds} component(s) out of bounds";
    }

    // ── Public helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Applies a chip size (in micrometers) directly — used during file load to restore
    /// a saved chip size without overwriting the user preference default.
    /// </summary>
    public void ApplyFromMicrometers(double widthUm, double heightUm)
    {
        _widthMillimeters  = widthUm  / 1000.0;
        _heightMillimeters = heightUm / 1000.0;
        _selectedPreset    = FindMatchingPreset(_widthMillimeters, _heightMillimeters);

        OnPropertyChanged(nameof(WidthMillimeters));
        OnPropertyChanged(nameof(HeightMillimeters));
        OnPropertyChanged(nameof(TileColumns));
        OnPropertyChanged(nameof(TileRows));
        OnPropertyChanged(nameof(SelectedPreset));

        ApplyToCanvas(widthUm, heightUm);
    }

    /// <summary>Current chip width in micrometers.</summary>
    public double CurrentWidthMicrometers => _widthMillimeters * 1000.0;

    /// <summary>Current chip height in micrometers.</summary>
    public double CurrentHeightMicrometers => _heightMillimeters * 1000.0;

    // ── Private helpers ───────────────────────────────────────────────────

    private void ApplyToCanvas(double widthUm, double heightUm)
    {
        if (_canvas is null) return;

        _canvas.ChipMinX = 0;
        _canvas.ChipMinY = 0;
        _canvas.ChipMaxX = widthUm;
        _canvas.ChipMaxY = heightUm;

        RefreshOutOfBoundsFlags(widthUm, heightUm);
        _canvas.InitializeAStarRouting(0, 0, widthUm, heightUm);
    }

    private void RefreshOutOfBoundsFlags(double widthUm, double heightUm)
    {
        if (_canvas is null) return;

        foreach (var vm in _canvas.Components)
        {
            double right  = vm.X + vm.Width;
            double bottom = vm.Y + vm.Height;
            vm.IsOutOfBounds = vm.X < 0 || vm.Y < 0
                || right  > widthUm
                || bottom > heightUm;
        }
    }

    private static ChipSizePreset? FindMatchingPreset(double widthMm, double heightMm)
    {
        const double tolerance = 0.001;
        return ChipSizeConfiguration.Presets.FirstOrDefault(p =>
            !p.IsCustom
            && Math.Abs(p.WidthMm  - widthMm)  < tolerance
            && Math.Abs(p.HeightMm - heightMm) < tolerance);
    }
}
