using CAP_Core.Analysis;
using CAP_Core.Components;
using Shouldly;

namespace UnitTests.Analysis;

/// <summary>
/// Tests for FeedbackLoopDetector cycle detection in component graphs.
/// </summary>
public class FeedbackLoopDetectorTests
{
    private readonly FeedbackLoopDetector _detector = new();

    [Fact]
    public void CountFeedbackLoops_EmptyGraph_ReturnsZero()
    {
        // Arrange
        var graph = CreateGraph(0, new Dictionary<int, HashSet<int>>());

        // Act
        var count = _detector.CountFeedbackLoops(graph);

        // Assert
        count.ShouldBe(0);
    }

    [Fact]
    public void CountFeedbackLoops_LinearGraph_ReturnsZero()
    {
        // Arrange: 0 -> 1 -> 2
        var adjacency = new Dictionary<int, HashSet<int>>
        {
            { 0, new HashSet<int> { 1 } },
            { 1, new HashSet<int> { 2 } },
            { 2, new HashSet<int>() },
        };
        var graph = CreateGraph(3, adjacency);

        // Act
        var count = _detector.CountFeedbackLoops(graph);

        // Assert
        count.ShouldBe(0);
    }

    [Fact]
    public void CountFeedbackLoops_SelfLoop_ReturnsOne()
    {
        // Arrange: 0 -> 0
        var adjacency = new Dictionary<int, HashSet<int>>
        {
            { 0, new HashSet<int> { 0 } },
        };
        var graph = CreateGraph(1, adjacency);

        // Act
        var count = _detector.CountFeedbackLoops(graph);

        // Assert
        count.ShouldBe(1);
    }

    [Fact]
    public void CountFeedbackLoops_SimpleCycle_ReturnsOne()
    {
        // Arrange: 0 -> 1 -> 2 -> 0
        var adjacency = new Dictionary<int, HashSet<int>>
        {
            { 0, new HashSet<int> { 1 } },
            { 1, new HashSet<int> { 2 } },
            { 2, new HashSet<int> { 0 } },
        };
        var graph = CreateGraph(3, adjacency);

        // Act
        var count = _detector.CountFeedbackLoops(graph);

        // Assert
        count.ShouldBe(1);
    }

    [Fact]
    public void CountFeedbackLoops_BidirectionalEdges_CountsTwoBackEdges()
    {
        // Arrange: 0 <-> 1 (typical for waveguide connections)
        var adjacency = new Dictionary<int, HashSet<int>>
        {
            { 0, new HashSet<int> { 1 } },
            { 1, new HashSet<int> { 0 } },
        };
        var graph = CreateGraph(2, adjacency);

        // Act
        var count = _detector.CountFeedbackLoops(graph);

        // Assert
        count.ShouldBe(1);
    }

    [Fact]
    public void CountFeedbackLoops_TwoDisjointCycles_ReturnsTwo()
    {
        // Arrange: 0 -> 1 -> 0, 2 -> 3 -> 2
        var adjacency = new Dictionary<int, HashSet<int>>
        {
            { 0, new HashSet<int> { 1 } },
            { 1, new HashSet<int> { 0 } },
            { 2, new HashSet<int> { 3 } },
            { 3, new HashSet<int> { 2 } },
        };
        var graph = CreateGraph(4, adjacency);

        // Act
        var count = _detector.CountFeedbackLoops(graph);

        // Assert
        count.ShouldBe(2);
    }

    private static ComponentGraph CreateGraph(
        int nodeCount,
        Dictionary<int, HashSet<int>> adjacency)
    {
        var nodes = new List<Component>();
        for (int i = 0; i < nodeCount; i++)
        {
            nodes.Add(TestComponentFactory.CreateStraightWaveGuide());
        }
        return new ComponentGraph(nodes, adjacency, new List<int>(), new List<int>());
    }
}
