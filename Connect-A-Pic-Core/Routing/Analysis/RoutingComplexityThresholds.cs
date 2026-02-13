namespace CAP_Core.Routing.Analysis;

/// <summary>
/// Configurable thresholds for routing complexity warnings.
/// When metrics exceed these values, diagnostic warnings are generated.
/// </summary>
public class RoutingComplexityThresholds
{
    /// <summary>
    /// Maximum average bends per connection before a warning is emitted.
    /// Default: 4.0 equivalent 90-degree bends.
    /// </summary>
    public double MaxAverageBendsPerConnection { get; set; } = 4.0;

    /// <summary>
    /// Maximum congestion score (waveguide density) before a warning.
    /// Default: 0.3 (30% of routable area occupied by waveguides).
    /// </summary>
    public double MaxCongestionScore { get; set; } = 0.3;

    /// <summary>
    /// Maximum path length in micrometers before a warning.
    /// Default: 5000 µm (5 mm).
    /// </summary>
    public double MaxPathLengthMicrometers { get; set; } = 5000.0;

    /// <summary>
    /// Maximum total bend count before a warning.
    /// Default: 20.0 equivalent 90-degree bends.
    /// </summary>
    public double MaxTotalBendCount { get; set; } = 20.0;
}
