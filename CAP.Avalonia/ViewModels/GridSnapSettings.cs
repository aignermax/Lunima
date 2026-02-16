using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// Settings for optional grid snapping during component placement and drag.
/// Coordinates are snapped to multiples of <see cref="GridSizeMicrometers"/>.
/// </summary>
public partial class GridSnapSettings : ObservableObject
{
    /// <summary>
    /// Default grid snap size in micrometers.
    /// </summary>
    public const double DefaultGridSizeMicrometers = 50.0;

    /// <summary>
    /// Whether grid snapping is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>
    /// Grid snap size in micrometers. Must be greater than zero.
    /// </summary>
    [ObservableProperty]
    private double _gridSizeMicrometers = DefaultGridSizeMicrometers;

    partial void OnGridSizeMicrometersChanged(double value)
    {
        if (value <= 0)
        {
            GridSizeMicrometers = DefaultGridSizeMicrometers;
        }
    }

    /// <summary>
    /// Snaps a coordinate value to the nearest grid point.
    /// Returns the value unchanged if snapping is disabled.
    /// </summary>
    /// <param name="value">The coordinate value in micrometers.</param>
    /// <returns>The snapped coordinate value.</returns>
    public double Snap(double value)
    {
        if (!IsEnabled || GridSizeMicrometers <= 0)
            return value;

        return Math.Round(value / GridSizeMicrometers, MidpointRounding.AwayFromZero) * GridSizeMicrometers;
    }

    /// <summary>
    /// Snaps both X and Y coordinates to the nearest grid points.
    /// Returns the values unchanged if snapping is disabled.
    /// </summary>
    public (double x, double y) Snap(double x, double y)
    {
        return (Snap(x), Snap(y));
    }

    /// <summary>
    /// Toggles the <see cref="IsEnabled"/> state.
    /// </summary>
    public void Toggle()
    {
        IsEnabled = !IsEnabled;
    }
}
