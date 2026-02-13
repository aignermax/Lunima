namespace CAP_Core.Comparison;

/// <summary>
/// Computed metrics for a single design snapshot.
/// Captures complexity, component count, and connectivity information.
/// </summary>
public class DesignMetrics
{
    /// <summary>
    /// Total number of components in the design.
    /// </summary>
    public int ComponentCount { get; init; }

    /// <summary>
    /// Total number of waveguide connections.
    /// </summary>
    public int ConnectionCount { get; init; }

    /// <summary>
    /// Number of distinct component types used.
    /// </summary>
    public int UniqueComponentTypes { get; init; }

    /// <summary>
    /// Breakdown of component counts by template name.
    /// </summary>
    public IReadOnlyDictionary<string, int> ComponentCountByType { get; init; }
        = new Dictionary<string, int>();

    /// <summary>
    /// Average connections per component (connectivity density).
    /// </summary>
    public double AverageConnectionsPerComponent { get; init; }

    /// <summary>
    /// Estimated total insertion loss in dB based on component count heuristic.
    /// Uses a simplified model: 0.5 dB per connection as baseline estimate.
    /// </summary>
    public double EstimatedTotalLossDb { get; init; }

    /// <summary>
    /// Complexity score combining component count, connections, and type diversity.
    /// Higher means more complex.
    /// </summary>
    public double ComplexityScore { get; init; }
}
