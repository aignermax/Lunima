namespace CAP_Core.Comparison;

/// <summary>
/// Complete comparison result for two design snapshots.
/// Contains metrics for each design, the diff list, and a summary.
/// </summary>
public class ComparisonReport
{
    /// <summary>
    /// Name of Design A.
    /// </summary>
    public string DesignAName { get; }

    /// <summary>
    /// Name of Design B.
    /// </summary>
    public string DesignBName { get; }

    /// <summary>
    /// Computed metrics for Design A.
    /// </summary>
    public DesignMetrics MetricsA { get; }

    /// <summary>
    /// Computed metrics for Design B.
    /// </summary>
    public DesignMetrics MetricsB { get; }

    /// <summary>
    /// Topology differences between the two designs.
    /// </summary>
    public IReadOnlyList<TopologyDifference> Differences { get; }

    public ComparisonReport(
        string designAName,
        string designBName,
        DesignMetrics metricsA,
        DesignMetrics metricsB,
        IReadOnlyList<TopologyDifference> differences)
    {
        DesignAName = designAName;
        DesignBName = designBName;
        MetricsA = metricsA;
        MetricsB = metricsB;
        Differences = differences;
    }

    /// <summary>
    /// Builds a comparison report from two snapshots.
    /// </summary>
    public static ComparisonReport Create(
        DesignSnapshot a,
        DesignSnapshot b)
    {
        var metricsA = DesignMetricsCalculator.Calculate(a);
        var metricsB = DesignMetricsCalculator.Calculate(b);
        var differences = TopologyDiffEngine.Compare(a, b);

        return new ComparisonReport(
            a.Name, b.Name,
            metricsA, metricsB,
            differences);
    }
}
