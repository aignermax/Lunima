using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// How waveguides occupy grid cells: two waveguides can cover the same cells (a crossing, or a
/// blocked fallback line drawn straight across another wire) and removing one must leave the
/// other intact; and a wire must never rasterize with gaps a route could slip through.
/// </summary>
public class WaveguideObstacleSharedCellTests
{
    private const double CellSize = 4;
    private const double Width = 4;

    [Fact]
    public void RemovingBatch_PreservesUnrelatedCrossingsAndAllowsExactReplacement()
    {
        var bulk = new PathfindingGrid(0, 0, 400, 400, CellSize);
        var sequential = new PathfindingGrid(0, 0, 400, 400, CellSize);
        var horizontal = Guid.NewGuid();
        var vertical = Guid.NewGuid();
        var retained = Guid.NewGuid();
        foreach (var grid in new[] { bulk, sequential })
        {
            grid.AddWaveguideObstacle(horizontal, new[] { new StraightSegment(0, 200, 400, 200, 0) }, Width);
            grid.AddWaveguideObstacle(vertical, new[] { new StraightSegment(200, 0, 200, 400, 90) }, Width);
            grid.AddWaveguideObstacle(retained, new[] { new StraightSegment(0, 200, 400, 200, 0) }, Width);
        }

        bulk.RemoveWaveguideObstacles(new[] { horizontal, vertical, horizontal, Guid.NewGuid() });
        sequential.RemoveWaveguideObstacle(horizontal);
        sequential.RemoveWaveguideObstacle(vertical);
        AssertSameCells(bulk, sequential);
        var crossing = bulk.PhysicalToGrid(200, 200);
        bulk.GetCellState(crossing.gridX, crossing.gridY).ShouldBe((byte)2);
        bulk.GetWaveguideGeometries().Count.ShouldBe(1);

        foreach (var grid in new[] { bulk, sequential })
        {
            grid.AddWaveguideObstacle(horizontal, new[] { new StraightSegment(0, 100, 400, 100, 0) }, Width);
            grid.RemoveWaveguideObstacle(retained);
        }
        AssertSameCells(bulk, sequential);
        bulk.GetCellState(crossing.gridX, crossing.gridY).ShouldBe((byte)0);
    }

    [Fact]
    public void RemovingEntireBatch_LeavesNoOrphanedCellsOrGeometry()
    {
        var grid = new PathfindingGrid(0, 0, 400, 400, CellSize);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        grid.AddWaveguideObstacle(ids[0], new[] { new StraightSegment(0, 200, 400, 200, 0) }, Width);
        grid.AddWaveguideObstacle(ids[1], new[] { new StraightSegment(200, 0, 200, 400, 90) }, Width);

        grid.RemoveWaveguideObstacles(ids);

        grid.GetWaveguideGeometries().ShouldBeEmpty();
        AssertSameCells(grid, new PathfindingGrid(0, 0, 400, 400, CellSize));
        var version = grid.WaveguideVersion;
        grid.RemoveWaveguideObstacles(ids);
        grid.WaveguideVersion.ShouldBe(version, "removing absent obstacles is a no-op");
    }

    private static void AssertSameCells(PathfindingGrid actual, PathfindingGrid expected)
    {
        for (var x = 0; x < actual.Width; x++)
        for (var y = 0; y < actual.Height; y++)
            actual.GetCellState(x, y).ShouldBe(expected.GetCellState(x, y), $"cell ({x}, {y})");
    }

    [Fact]
    public void RemovingOneOfTwoCrossingWires_KeepsTheSharedCellsBlocked()
    {
        var grid = new PathfindingGrid(0, 0, 400, 400, CellSize);
        var horizontal = Guid.NewGuid();
        var vertical = Guid.NewGuid();
        grid.AddWaveguideObstacle(horizontal, new[] { new StraightSegment(0, 200, 400, 200, 0) }, Width);
        grid.AddWaveguideObstacle(vertical, new[] { new StraightSegment(200, 0, 200, 400, 90) }, Width);

        grid.RemoveWaveguideObstacle(vertical);

        var (gx, gy) = grid.PhysicalToGrid(200, 200);
        grid.GetCellState(gx, gy).ShouldBe((byte)2, "the horizontal wire still runs through the crossing cell");
        var (vx, vy) = grid.PhysicalToGrid(200, 100);
        grid.GetCellState(vx, vy).ShouldBe((byte)0, "the removed wire's own cells are free again");
    }

    [Fact]
    public void WireMidwayBetweenCellColumns_StillBlocksACellAlongItsWholeLength()
    {
        // Cell centres sit at 2, 6, 10 … µm; a 4-µm wire at x = 152 is exactly 2 µm from the
        // centres of both neighbouring columns, so the centre test alone marked none of them.
        var grid = new PathfindingGrid(0, 0, 400, 400, CellSize);
        grid.AddWaveguideObstacle(Guid.NewGuid(), new[] { new StraightSegment(152, 20, 152, 380, 90) }, Width);

        for (double y = 40; y <= 360; y += CellSize)
        {
            var (gx, gy) = grid.PhysicalToGrid(152, y);
            grid.GetCellState(gx, gy).ShouldBe((byte)2, $"the wire must leave no gap at y = {y}");
        }
    }

    [Fact]
    public void ClearingAllWires_FreesEveryWaveguideCell()
    {
        var grid = new PathfindingGrid(0, 0, 400, 400, CellSize);
        grid.AddWaveguideObstacle(Guid.NewGuid(), new[] { new StraightSegment(0, 200, 400, 200, 0) }, Width);
        grid.AddWaveguideObstacle(Guid.NewGuid(), new[] { new StraightSegment(200, 0, 200, 400, 90) }, Width);

        grid.ClearAllWaveguideObstacles();

        var (gx, gy) = grid.PhysicalToGrid(200, 200);
        grid.GetCellState(gx, gy).ShouldBe((byte)0);
    }
}
