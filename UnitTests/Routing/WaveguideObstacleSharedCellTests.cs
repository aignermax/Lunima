using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// Two waveguides can cover the same cells — a crossing, or a blocked fallback line drawn
/// straight across another wire. Removing one must leave the other wire intact in the grid.
/// </summary>
public class WaveguideObstacleSharedCellTests
{
    private const double CellSize = 4;
    private const double Width = 4;

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
