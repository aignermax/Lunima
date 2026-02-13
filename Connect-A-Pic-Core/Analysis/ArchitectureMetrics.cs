namespace CAP_Core.Analysis;

/// <summary>
/// Immutable result containing all architecture complexity metrics for a photonic circuit.
/// </summary>
public class ArchitectureMetrics
{
    /// <summary>
    /// Total number of components placed in the grid.
    /// </summary>
    public int TotalComponentCount { get; }

    /// <summary>
    /// Number of components grouped by their TypeNumber.
    /// </summary>
    public IReadOnlyDictionary<int, int> ComponentCountByType { get; }

    /// <summary>
    /// Longest path length (in component hops) from any input to any output.
    /// Zero if no input-to-output path exists.
    /// </summary>
    public int NetworkDepth { get; }

    /// <summary>
    /// Number of outgoing connections per component, keyed by component.
    /// </summary>
    public IReadOnlyDictionary<int, int> FanOutDistribution { get; }

    /// <summary>
    /// Number of feedback loops (directed cycles) detected in the circuit.
    /// </summary>
    public int FeedbackLoopCount { get; }

    /// <summary>
    /// Lengths of all distinct paths from inputs to outputs.
    /// Empty if no input-to-output paths exist.
    /// </summary>
    public IReadOnlyList<int> PathLengths { get; }

    /// <summary>
    /// Average path length from input to output. Zero if no paths exist.
    /// </summary>
    public double AveragePathLength { get; }

    /// <summary>
    /// Creates a new architecture metrics result.
    /// </summary>
    public ArchitectureMetrics(
        int totalComponentCount,
        IReadOnlyDictionary<int, int> componentCountByType,
        int networkDepth,
        IReadOnlyDictionary<int, int> fanOutDistribution,
        int feedbackLoopCount,
        IReadOnlyList<int> pathLengths,
        double averagePathLength)
    {
        TotalComponentCount = totalComponentCount;
        ComponentCountByType = componentCountByType;
        NetworkDepth = networkDepth;
        FanOutDistribution = fanOutDistribution;
        FeedbackLoopCount = feedbackLoopCount;
        PathLengths = pathLengths;
        AveragePathLength = averagePathLength;
    }
}
