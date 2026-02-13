namespace CAP_Core.Comparison;

/// <summary>
/// Calculates architecture-level metrics for a design snapshot.
/// </summary>
public static class DesignMetricsCalculator
{
    /// <summary>
    /// Estimated insertion loss per waveguide connection in dB.
    /// Conservative baseline for architecture-level comparison.
    /// </summary>
    private const double EstimatedLossPerConnectionDb = 0.5;

    /// <summary>
    /// Weight for connection count in complexity score.
    /// </summary>
    private const double ConnectionComplexityWeight = 1.5;

    /// <summary>
    /// Weight for unique component types in complexity score.
    /// </summary>
    private const double TypeDiversityWeight = 2.0;

    /// <summary>
    /// Calculates metrics for the given design snapshot.
    /// </summary>
    public static DesignMetrics Calculate(DesignSnapshot snapshot)
    {
        var componentCount = snapshot.Components.Count;
        var connectionCount = snapshot.Connections.Count;

        var countByType = snapshot.Components
            .GroupBy(c => c.TemplateName)
            .ToDictionary(g => g.Key, g => g.Count());

        var uniqueTypes = countByType.Count;

        double avgConnections = componentCount > 0
            ? (double)connectionCount / componentCount
            : 0;

        double estimatedLoss = connectionCount * EstimatedLossPerConnectionDb;

        double complexity = componentCount
            + connectionCount * ConnectionComplexityWeight
            + uniqueTypes * TypeDiversityWeight;

        return new DesignMetrics
        {
            ComponentCount = componentCount,
            ConnectionCount = connectionCount,
            UniqueComponentTypes = uniqueTypes,
            ComponentCountByType = countByType,
            AverageConnectionsPerComponent = Math.Round(avgConnections, 2),
            EstimatedTotalLossDb = Math.Round(estimatedLoss, 2),
            ComplexityScore = Math.Round(complexity, 2)
        };
    }
}
