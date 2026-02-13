using CAP_Core.Components;
using CAP_Core.Grid;

namespace CAP_Core.Analysis;

/// <summary>
/// Calculates architecture complexity metrics for a photonic circuit grid.
/// Orchestrates graph building, depth analysis, and cycle detection.
/// </summary>
public class ArchitectureMetricsCalculator
{
    private readonly ComponentGraphBuilder _graphBuilder;
    private readonly NetworkDepthCalculator _depthCalculator;
    private readonly FeedbackLoopDetector _feedbackDetector;

    /// <summary>
    /// Creates a calculator with injected dependencies.
    /// </summary>
    public ArchitectureMetricsCalculator(
        ComponentGraphBuilder graphBuilder,
        NetworkDepthCalculator depthCalculator,
        FeedbackLoopDetector feedbackDetector)
    {
        _graphBuilder = graphBuilder;
        _depthCalculator = depthCalculator;
        _feedbackDetector = feedbackDetector;
    }

    /// <summary>
    /// Creates a calculator from a GridManager for convenience.
    /// </summary>
    public static ArchitectureMetricsCalculator FromGridManager(
        GridManager gridManager)
    {
        var graphBuilder = new ComponentGraphBuilder(
            gridManager.TileManager,
            gridManager.ComponentRelationshipManager,
            gridManager.ExternalPortManager);

        return new ArchitectureMetricsCalculator(
            graphBuilder,
            new NetworkDepthCalculator(),
            new FeedbackLoopDetector());
    }

    /// <summary>
    /// Calculates all architecture metrics for the current grid state.
    /// </summary>
    public ArchitectureMetrics Calculate()
    {
        var graph = _graphBuilder.Build();

        var componentCountByType = CalculateComponentCountByType(graph);
        var fanOutDistribution = CalculateFanOutDistribution(graph);
        var pathLengths = _depthCalculator.FindAllPathLengths(graph);
        var networkDepth = pathLengths.Count > 0 ? pathLengths.Max() : 0;
        var feedbackLoopCount = _feedbackDetector.CountFeedbackLoops(graph);
        var averagePathLength = pathLengths.Count > 0
            ? pathLengths.Average()
            : 0.0;

        return new ArchitectureMetrics(
            totalComponentCount: graph.Nodes.Count,
            componentCountByType: componentCountByType,
            networkDepth: networkDepth,
            fanOutDistribution: fanOutDistribution,
            feedbackLoopCount: feedbackLoopCount,
            pathLengths: pathLengths,
            averagePathLength: averagePathLength);
    }

    private static Dictionary<int, int> CalculateComponentCountByType(
        ComponentGraph graph)
    {
        var counts = new Dictionary<int, int>();
        foreach (var component in graph.Nodes)
        {
            var type = component.TypeNumber;
            counts.TryGetValue(type, out var current);
            counts[type] = current + 1;
        }
        return counts;
    }

    private static Dictionary<int, int> CalculateFanOutDistribution(
        ComponentGraph graph)
    {
        var distribution = new Dictionary<int, int>();
        foreach (var entry in graph.AdjacencyList)
        {
            var fanOut = entry.Value.Count;
            distribution.TryGetValue(fanOut, out var current);
            distribution[fanOut] = current + 1;
        }
        return distribution;
    }
}
