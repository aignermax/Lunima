using CommunityToolkit.Mvvm.ComponentModel;
using CAP_Core.Routing;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// ViewModel for routing algorithm settings.
/// Allows runtime configuration of pathfinding parameters.
/// </summary>
public partial class RoutingSettingsViewModel : ObservableObject
{
    /// <summary>
    /// Grid cell size in micrometers for A* pathfinding.
    /// Smaller = more precise but slower. Larger = faster but less precise.
    /// Recommended range: 2-10µm.
    /// </summary>
    [ObservableProperty]
    private double _cellSizeMicrometers = 4.0;

    /// <summary>
    /// Whether to use hierarchical pathfinding (HPA*) for long-distance routes.
    /// When disabled, uses flat A* for all routes (simpler, more predictable).
    /// </summary>
    [ObservableProperty]
    private bool _useHierarchicalPathfinding = false;

    /// <summary>
    /// Minimum bend radius in micrometers.
    /// </summary>
    [ObservableProperty]
    private double _minBendRadiusMicrometers = 10.0;

    /// <summary>
    /// Minimum spacing between waveguides in micrometers.
    /// </summary>
    [ObservableProperty]
    private double _minWaveguideSpacingMicrometers = 2.0;

    /// <summary>
    /// Obstacle padding around components in micrometers.
    /// Larger = more clearance but harder to route in tight spaces.
    /// </summary>
    [ObservableProperty]
    private double _obstaclePaddingMicrometers = 5.0;

    /// <summary>
    /// Applies the current settings to the given router.
    /// </summary>
    public void ApplyToRouter(WaveguideRouter router)
    {
        router.AStarCellSize = CellSizeMicrometers;
        router.UseHierarchicalPathfinding = UseHierarchicalPathfinding;
        router.MinBendRadiusMicrometers = MinBendRadiusMicrometers;
        router.MinWaveguideSpacingMicrometers = MinWaveguideSpacingMicrometers;
        router.ObstaclePaddingMicrometers = ObstaclePaddingMicrometers;
    }

    /// <summary>
    /// Predefined cell size presets for quick selection.
    /// </summary>
    public static readonly (string Name, double Value, string Description)[] CellSizePresets =
    {
        ("Very Fine (2µm)", 2.0, "Highest precision, slowest"),
        ("Fine (3µm)", 3.0, "Good precision, moderate speed"),
        ("Balanced (4µm)", 4.0, "Default: good balance"),
        ("Fast (5µm)", 5.0, "Faster routing, less precise"),
        ("Very Fast (8µm)", 8.0, "Fastest, lowest precision"),
        ("Ultra Fast (10µm)", 10.0, "Maximum speed, minimum precision")
    };
}
