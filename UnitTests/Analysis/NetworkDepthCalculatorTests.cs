using CAP_Core.Analysis;
using CAP_Core.Components;
using Shouldly;

namespace UnitTests.Analysis;

/// <summary>
/// Tests for NetworkDepthCalculator path length and depth analysis.
/// </summary>
public class NetworkDepthCalculatorTests
{
    private readonly NetworkDepthCalculator _calculator = new();

    [Fact]
    public void CalculateNetworkDepth_EmptyGraph_ReturnsZero()
    {
        // Arrange
        var graph = CreateGraph(0, new(), new List<int>(), new List<int>());

        // Act
        var depth = _calculator.CalculateNetworkDepth(graph);

        // Assert
        depth.ShouldBe(0);
    }

    [Fact]
    public void CalculateNetworkDepth_NoInputsOrOutputs_ReturnsZero()
    {
        // Arrange: 0 -> 1 -> 2, but no input/output designation
        var adjacency = new Dictionary<int, HashSet<int>>
        {
            { 0, new HashSet<int> { 1 } },
            { 1, new HashSet<int> { 2 } },
            { 2, new HashSet<int>() },
        };
        var graph = CreateGraph(3, adjacency, new List<int>(), new List<int>());

        // Act
        var depth = _calculator.CalculateNetworkDepth(graph);

        // Assert
        depth.ShouldBe(0);
    }

    [Fact]
    public void CalculateNetworkDepth_DirectInputToOutput_ReturnsZero()
    {
        // Arrange: single node is both input and output
        var adjacency = new Dictionary<int, HashSet<int>>
        {
            { 0, new HashSet<int>() },
        };
        var graph = CreateGraph(1, adjacency, new List<int> { 0 }, new List<int> { 0 });

        // Act
        var depth = _calculator.CalculateNetworkDepth(graph);

        // Assert
        depth.ShouldBe(0);
    }

    [Fact]
    public void CalculateNetworkDepth_LinearChain_ReturnsChainLength()
    {
        // Arrange: 0 -> 1 -> 2, input=0, output=2
        var adjacency = new Dictionary<int, HashSet<int>>
        {
            { 0, new HashSet<int> { 1 } },
            { 1, new HashSet<int> { 2 } },
            { 2, new HashSet<int>() },
        };
        var graph = CreateGraph(3, adjacency, new List<int> { 0 }, new List<int> { 2 });

        // Act
        var depth = _calculator.CalculateNetworkDepth(graph);

        // Assert
        depth.ShouldBe(2);
    }

    [Fact]
    public void FindAllPathLengths_TwoOutputs_FindsBothPaths()
    {
        // Arrange: 0 -> 1 -> 2, 0 -> 1 -> 3; input=0, outputs=2,3
        var adjacency = new Dictionary<int, HashSet<int>>
        {
            { 0, new HashSet<int> { 1 } },
            { 1, new HashSet<int> { 2, 3 } },
            { 2, new HashSet<int>() },
            { 3, new HashSet<int>() },
        };
        var graph = CreateGraph(4, adjacency,
            new List<int> { 0 }, new List<int> { 2, 3 });

        // Act
        var paths = _calculator.FindAllPathLengths(graph);

        // Assert
        paths.Count.ShouldBe(2);
        paths.ShouldContain(2);
    }

    [Fact]
    public void CalculateNetworkDepth_BranchingPaths_ReturnsLongest()
    {
        // Arrange: 0 -> 1 -> 2 -> 3, 0 -> 4; input=0, outputs=3,4
        var adjacency = new Dictionary<int, HashSet<int>>
        {
            { 0, new HashSet<int> { 1, 4 } },
            { 1, new HashSet<int> { 2 } },
            { 2, new HashSet<int> { 3 } },
            { 3, new HashSet<int>() },
            { 4, new HashSet<int>() },
        };
        var graph = CreateGraph(5, adjacency,
            new List<int> { 0 }, new List<int> { 3, 4 });

        // Act
        var depth = _calculator.CalculateNetworkDepth(graph);

        // Assert
        depth.ShouldBe(3);
    }

    [Fact]
    public void FindAllPathLengths_NoPathToOutput_ReturnsEmpty()
    {
        // Arrange: 0 -> 1, but output at 2 (disconnected)
        var adjacency = new Dictionary<int, HashSet<int>>
        {
            { 0, new HashSet<int> { 1 } },
            { 1, new HashSet<int>() },
            { 2, new HashSet<int>() },
        };
        var graph = CreateGraph(3, adjacency,
            new List<int> { 0 }, new List<int> { 2 });

        // Act
        var paths = _calculator.FindAllPathLengths(graph);

        // Assert
        paths.Count.ShouldBe(0);
    }

    private static ComponentGraph CreateGraph(
        int nodeCount,
        Dictionary<int, HashSet<int>> adjacency,
        List<int> inputs,
        List<int> outputs)
    {
        var nodes = new List<Component>();
        for (int i = 0; i < nodeCount; i++)
        {
            nodes.Add(TestComponentFactory.CreateStraightWaveGuide());
        }
        return new ComponentGraph(nodes, adjacency, inputs, outputs);
    }
}
