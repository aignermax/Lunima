using CAP_Core.Analysis;
using CAP_Core.Components;
using CAP_Core.Grid;
using Shouldly;

namespace UnitTests.Analysis;

/// <summary>
/// Tests for ComponentGraphBuilder graph extraction from grid topology.
/// </summary>
public class ComponentGraphBuilderTests
{
    [Fact]
    public void Build_EmptyGrid_ReturnsEmptyGraph()
    {
        // Arrange
        var grid = new GridManager(10, 10);
        var builder = CreateBuilder(grid);

        // Act
        var graph = builder.Build();

        // Assert
        graph.Nodes.Count.ShouldBe(0);
        graph.AdjacencyList.Count.ShouldBe(0);
    }

    [Fact]
    public void Build_SingleComponent_ReturnsOneNodeNoEdges()
    {
        // Arrange
        var grid = new GridManager(10, 10);
        var wg = TestComponentFactory.CreateStraightWaveGuide();
        grid.ComponentMover.PlaceComponent(3, 3, wg);
        var builder = CreateBuilder(grid);

        // Act
        var graph = builder.Build();

        // Assert
        graph.Nodes.Count.ShouldBe(1);
        graph.AdjacencyList[0].Count.ShouldBe(0);
    }

    [Fact]
    public void Build_TwoConnectedWaveguides_CreatesBidirectionalEdges()
    {
        // Arrange
        var grid = new GridManager(10, 10);
        var wg1 = TestComponentFactory.CreateStraightWaveGuide();
        var wg2 = TestComponentFactory.CreateStraightWaveGuide();
        grid.ComponentMover.PlaceComponent(3, 3, wg1);
        grid.ComponentMover.PlaceComponent(4, 3, wg2);
        var builder = CreateBuilder(grid);

        // Act
        var graph = builder.Build();

        // Assert
        graph.Nodes.Count.ShouldBe(2);
        graph.AdjacencyList[0].ShouldContain(1);
        graph.AdjacencyList[1].ShouldContain(0);
    }

    [Fact]
    public void Build_ThreeChainedWaveguides_CreatesLinearGraph()
    {
        // Arrange
        var grid = new GridManager(10, 10);
        var wg1 = TestComponentFactory.CreateStraightWaveGuide();
        var wg2 = TestComponentFactory.CreateStraightWaveGuide();
        var wg3 = TestComponentFactory.CreateStraightWaveGuide();
        grid.ComponentMover.PlaceComponent(3, 3, wg1);
        grid.ComponentMover.PlaceComponent(4, 3, wg2);
        grid.ComponentMover.PlaceComponent(5, 3, wg3);
        var builder = CreateBuilder(grid);

        // Act
        var graph = builder.Build();

        // Assert
        graph.Nodes.Count.ShouldBe(3);
        var idx1 = graph.Nodes.ToList().IndexOf(wg1);
        var idx2 = graph.Nodes.ToList().IndexOf(wg2);
        var idx3 = graph.Nodes.ToList().IndexOf(wg3);
        graph.AdjacencyList[idx1].ShouldContain(idx2);
        graph.AdjacencyList[idx2].ShouldContain(idx1);
        graph.AdjacencyList[idx2].ShouldContain(idx3);
        graph.AdjacencyList[idx3].ShouldContain(idx2);
    }

    [Fact]
    public void Build_DisconnectedComponents_NoEdges()
    {
        // Arrange
        var grid = new GridManager(10, 10);
        var wg1 = TestComponentFactory.CreateStraightWaveGuide();
        var wg2 = TestComponentFactory.CreateStraightWaveGuide();
        grid.ComponentMover.PlaceComponent(1, 1, wg1);
        grid.ComponentMover.PlaceComponent(5, 5, wg2);
        var builder = CreateBuilder(grid);

        // Act
        var graph = builder.Build();

        // Assert
        graph.Nodes.Count.ShouldBe(2);
        graph.AdjacencyList[0].Count.ShouldBe(0);
        graph.AdjacencyList[1].Count.ShouldBe(0);
    }

    [Fact]
    public void Build_ComponentAtInputPort_IdentifiesInputNode()
    {
        // Arrange - external inputs are at x=0, y=2,3,4
        var grid = new GridManager(10, 10);
        var wg = TestComponentFactory.CreateStraightWaveGuide();
        grid.ComponentMover.PlaceComponent(0, 2, wg);
        var builder = CreateBuilder(grid);

        // Act
        var graph = builder.Build();

        // Assert
        graph.InputNodeIndices.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Build_ComponentAtOutputPort_IdentifiesOutputNode()
    {
        // Arrange - external outputs are at x=width-1, y=5,6,7,8,9
        var grid = new GridManager(10, 10);
        var wg = TestComponentFactory.CreateStraightWaveGuide();
        grid.ComponentMover.PlaceComponent(9, 5, wg);
        var builder = CreateBuilder(grid);

        // Act
        var graph = builder.Build();

        // Assert
        graph.OutputNodeIndices.Count.ShouldBeGreaterThan(0);
    }

    private static ComponentGraphBuilder CreateBuilder(GridManager grid)
    {
        return new ComponentGraphBuilder(
            grid.TileManager,
            grid.ComponentRelationshipManager,
            grid.ExternalPortManager);
    }
}
