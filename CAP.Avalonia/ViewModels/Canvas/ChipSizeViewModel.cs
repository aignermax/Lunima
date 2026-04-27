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
    private const double UmPerMm = 1000.0;

    private readonly UserPreferencesService _preferences;
    private readonly DesignCanvasViewModel _canvas;
    private readonly ErrorConsoleService? _errorConsole;

    // ── Observable fields (millimeters, user-facing) ─────────────────────
    [ObservableProperty] private double _widthMillimeters;
    [ObservableProperty] private double _heightMillimeters;
    [ObservableProperty] private ChipSizePreset? _selectedPreset;
    [ObservableProperty] private string _statusText = "";

    // ── Derived (read-only) ───────────────────────────────────────────────
    /// <summary>Number of tile columns at 250 μm pitch.</summary>
    public int TileColumns
        => (int)(_widthMillimeters * UmPerMm / PhotonicConstants.GridSizeMicrometers);

    /// <summary>Number of tile rows at 250 μm pitch.</summary>
    public int TileRows
        => (int)(_heightMillimeters * UmPerMm / PhotonicConstants.GridSizeMicrometers);

    /// <summary>Available MPW / shuttle-mask presets plus a Custom entry.</summary>
    public ObservableCollection<ChipSizePreset> Presets { get; }
        = new(ChipSizeConfiguration.Presets);

    // ── Constructor ───────────────────────────────────────────────────────

    /// <summary>
    /// Initializes the ViewModel from persisted user defaults and applies the
    /// initial chip size to the canvas. Both dependencies are required — there
    /// is no two-phase init.
    /// </summary>
    public ChipSizeViewModel(
        UserPreferencesService preferences,
        DesignCanvasViewModel canvas,
        ErrorConsoleService? errorConsole = null)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _errorConsole = errorConsole;

        _widthMillimeters  = preferences.GetDefaultChipWidthMm();
        _heightMillimeters = preferences.GetDefaultChipHeightMm();
        _selectedPreset    = FindMatchingPreset(_widthMillimeters, _heightMillimeters);

        ApplyToCanvas(_widthMillimeters * UmPerMm, _heightMillimeters * UmPerMm);
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
    /// Applies the current width × height to the canvas and saves the user default.
    /// Reports a count of out-of-bounds components in the StatusText.
    /// </summary>
    [RelayCommand]
    private void Apply()
    {
        double widthUm  = _widthMillimeters  * UmPerMm;
        double heightUm = _heightMillimeters * UmPerMm;

        if (!TryValidate(widthUm, heightUm, out var error))
        {
            StatusText = error;
            return;
        }

        ApplyToCanvas(widthUm, heightUm);
        _preferences.SetDefaultChipSize(_widthMillimeters, _heightMillimeters);

        int outOfBounds = CountComponentsOutsideBounds(widthUm, heightUm);

        StatusText = outOfBounds == 0
            ? $"Chip size applied: {TileColumns} × {TileRows} tiles"
            : $"Chip size applied — {outOfBounds} component(s) out of bounds (see Design Checks)";
    }

    // ── Public helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Applies a chip size (in micrometers) directly — used during file load to restore
    /// a saved chip size without overwriting the user preference default. Invalid values
    /// (NaN, infinity, sub-tile, over-max) fall back to the persisted user default and
    /// are logged via the error console.
    /// </summary>
    public void ApplyFromMicrometers(double widthUm, double heightUm)
    {
        if (!TryValidate(widthUm, heightUm, out var error))
        {
            _errorConsole?.LogWarning(
                $"Saved chip size {widthUm}×{heightUm} µm is invalid ({error}); falling back to user default.");
            widthUm  = _widthMillimeters  * UmPerMm;
            heightUm = _heightMillimeters * UmPerMm;
        }

        _widthMillimeters  = widthUm  / UmPerMm;
        _heightMillimeters = heightUm / UmPerMm;
        _selectedPreset    = FindMatchingPreset(_widthMillimeters, _heightMillimeters);

        OnPropertyChanged(nameof(WidthMillimeters));
        OnPropertyChanged(nameof(HeightMillimeters));
        OnPropertyChanged(nameof(TileColumns));
        OnPropertyChanged(nameof(TileRows));
        OnPropertyChanged(nameof(SelectedPreset));

        ApplyToCanvas(widthUm, heightUm);
    }

    /// <summary>Current chip width in micrometers.</summary>
    public double CurrentWidthMicrometers => _widthMillimeters * UmPerMm;

    /// <summary>Current chip height in micrometers.</summary>
    public double CurrentHeightMicrometers => _heightMillimeters * UmPerMm;

    // ── Private helpers ───────────────────────────────────────────────────

    private static bool TryValidate(double widthUm, double heightUm, out string error)
    {
        if (double.IsNaN(widthUm) || double.IsNaN(heightUm) ||
            double.IsInfinity(widthUm) || double.IsInfinity(heightUm))
        {
            error = "dimensions must be finite";
            return false;
        }
        if (widthUm < ChipSizeConfiguration.MinDimensionMicrometers ||
            heightUm < ChipSizeConfiguration.MinDimensionMicrometers)
        {
            error = $"dimensions must be at least one tile ({ChipSizeConfiguration.MinDimensionMicrometers} µm)";
            return false;
        }
        if (widthUm > ChipSizeConfiguration.MaxDimensionMicrometers ||
            heightUm > ChipSizeConfiguration.MaxDimensionMicrometers)
        {
            error = $"dimensions must not exceed {ChipSizeConfiguration.MaxDimensionMicrometers} µm (100 mm)";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private void ApplyToCanvas(double widthUm, double heightUm)
    {
        _canvas.ChipMinX = 0;
        _canvas.ChipMinY = 0;
        _canvas.ChipMaxX = widthUm;
        _canvas.ChipMaxY = heightUm;

        _canvas.InitializeAStarRouting(0, 0, widthUm, heightUm);

        // ChipMaxX/Y on DesignCanvasViewModel are plain forwarder properties without
        // PropertyChanged notifications, and DesignCanvas only invalidates on a small
        // allow-list of bound properties — so a fresh chip size never reaches the
        // GridRenderer without an explicit repaint request.
        _canvas.RepaintRequested?.Invoke();
    }

    // The authoritative out-of-bounds check lives in DesignValidator.ValidateComponentBounds
    // (which the Design Checks panel surfaces). This count is purely the post-apply summary
    // shown in StatusText — kept inline so it can't go stale relative to the canvas state.
    private int CountComponentsOutsideBounds(double widthUm, double heightUm)
    {
        int count = 0;
        foreach (var vm in _canvas.Components)
        {
            double right  = vm.X + vm.Width;
            double bottom = vm.Y + vm.Height;
            if (vm.X < 0 || vm.Y < 0 || right > widthUm || bottom > heightUm)
                count++;
        }
        return count;
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
