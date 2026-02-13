using CAP_Core.Components;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing.Analysis;

/// <summary>
/// Analyzes routing complexity metrics for a set of waveguide connections.
/// Produces warnings when thresholds are exceeded, indicating designs
/// that may be hard to realize physically.
/// </summary>
public class RoutingComplexityAnalyzer
{
    private readonly RoutingComplexityThresholds _thresholds;

    /// <summary>
    /// Creates an analyzer with the specified thresholds.
    /// </summary>
    /// <param name="thresholds">Warning thresholds for complexity metrics.</param>
    public RoutingComplexityAnalyzer(RoutingComplexityThresholds thresholds)
    {
        _thresholds = thresholds;
    }

    /// <summary>
    /// Creates an analyzer with default thresholds.
    /// </summary>
    public RoutingComplexityAnalyzer()
        : this(new RoutingComplexityThresholds())
    {
    }

    /// <summary>
    /// Analyzes the routing complexity of the given connections.
    /// </summary>
    /// <param name="connections">The waveguide connections to analyze.</param>
    /// <param name="grid">
    /// Optional pathfinding grid for congestion calculation.
    /// When null, congestion score is reported as 0.
    /// </param>
    /// <returns>A result containing metrics and diagnostic entries.</returns>
    public RoutingComplexityResult Analyze(
        IReadOnlyList<WaveguideConnection> connections,
        PathfindingGrid? grid = null)
    {
        var metrics = ComputeMetrics(connections, grid);
        var result = BuildResult(metrics);
        EvaluateThresholds(result);
        return result;
    }

    private RoutingMetrics ComputeMetrics(
        IReadOnlyList<WaveguideConnection> connections,
        PathfindingGrid? grid)
    {
        double totalBends = 0;
        double longestPath = 0;
        int blockedFallbackCount = 0;

        foreach (var connection in connections)
        {
            if (connection.RoutedPath == null) continue;

            totalBends += connection.RoutedPath.TotalEquivalent90DegreeBends;

            double length = connection.RoutedPath.TotalLengthMicrometers;
            if (length > longestPath)
                longestPath = length;

            if (connection.IsBlockedFallback)
                blockedFallbackCount++;
        }

        double avgBends = connections.Count > 0
            ? totalBends / connections.Count
            : 0;

        double congestion = grid != null
            ? CalculateCongestionScore(grid)
            : 0;

        return new RoutingMetrics(
            totalBends, avgBends, congestion,
            longestPath, connections.Count, blockedFallbackCount);
    }

    private static double CalculateCongestionScore(PathfindingGrid grid)
    {
        int waveguideCells = 0;
        int routableCells = 0;

        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                byte state = grid.GetCellState(x, y);
                if (state != 1) // Not a component obstacle
                    routableCells++;
                if (state == 2) // Waveguide
                    waveguideCells++;
            }
        }

        return routableCells > 0
            ? (double)waveguideCells / routableCells
            : 0;
    }

    private static RoutingComplexityResult BuildResult(RoutingMetrics metrics)
    {
        return new RoutingComplexityResult
        {
            TotalBendCount = metrics.TotalBends,
            AverageBendsPerConnection = metrics.AverageBends,
            CongestionScore = metrics.Congestion,
            LongestPathMicrometers = metrics.LongestPath,
            ConnectionCount = metrics.ConnectionCount,
            BlockedFallbackCount = metrics.BlockedFallbackCount
        };
    }

    private void EvaluateThresholds(RoutingComplexityResult result)
    {
        if (result.ConnectionCount == 0) return;

        if (result.TotalBendCount > _thresholds.MaxTotalBendCount)
        {
            result.AddWarning(
                $"Total bend count ({result.TotalBendCount:F1}) exceeds " +
                $"threshold ({_thresholds.MaxTotalBendCount:F1}).");
        }

        if (result.AverageBendsPerConnection > _thresholds.MaxAverageBendsPerConnection)
        {
            result.AddWarning(
                $"Average bends per connection ({result.AverageBendsPerConnection:F2}) " +
                $"exceeds threshold ({_thresholds.MaxAverageBendsPerConnection:F2}).");
        }

        if (result.CongestionScore > _thresholds.MaxCongestionScore)
        {
            result.AddWarning(
                $"Routing congestion ({result.CongestionScore:P1}) exceeds " +
                $"threshold ({_thresholds.MaxCongestionScore:P1}).");
        }

        if (result.LongestPathMicrometers > _thresholds.MaxPathLengthMicrometers)
        {
            result.AddWarning(
                $"Longest path ({result.LongestPathMicrometers:F0} µm) exceeds " +
                $"threshold ({_thresholds.MaxPathLengthMicrometers:F0} µm).");
        }

        if (result.BlockedFallbackCount > 0)
        {
            result.AddError(
                $"{result.BlockedFallbackCount} connection(s) use fallback paths " +
                $"through obstacles.");
        }
    }

    /// <summary>
    /// Internal record for intermediate metric computation.
    /// </summary>
    private record RoutingMetrics(
        double TotalBends,
        double AverageBends,
        double Congestion,
        double LongestPath,
        int ConnectionCount,
        int BlockedFallbackCount);
}
