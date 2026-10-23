using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

public class WaveguideObstacleRestoreTests
{
    [Fact]
    public void SnapshotBatch_OnlyRebuildsChangedRoutesAndPreservesUnrelatedRegistrations()
    {
        var grid = Grid();
        var unchanged = Guid.NewGuid();
        var changed = Guid.NewGuid();
        var removed = Guid.NewGuid();
        var unrelated = Guid.NewGuid();
        grid.AddWaveguideObstacle(unchanged, Line(20), 4);
        grid.AddWaveguideObstacle(changed, Line(40), 4);
        grid.AddWaveguideObstacle(removed, Line(60), 4);
        grid.AddWaveguideObstacle(unrelated, Line(80), 4);
        var added = 0;
        grid.OnWaveguideCellsAdded += _ => added++;

        grid.RestoreWaveguideObstacles(new (Guid, IReadOnlyList<PathSegment>?, double)[]
        {
            (unchanged, Line(20), 4), (changed, Line(50), 4), (removed, null, 4)
        });

        added.ShouldBe(1);
        var expected = Grid();
        expected.AddWaveguideObstacle(unchanged, Line(20), 4);
        expected.AddWaveguideObstacle(changed, Line(50), 4);
        expected.AddWaveguideObstacle(unrelated, Line(80), 4);
        AssertSameCells(grid, expected);
        grid.GetWaveguideGeometries().Count.ShouldBe(3);
    }

    [Fact]
    public void ExactRestoration_ReusesRasterWithoutInvalidatingUnchangedGrid()
    {
        var grid = Grid();
        var id = Guid.NewGuid();
        grid.AddWaveguideObstacle(id, Line(50), 4);
        var added = 0;
        grid.OnWaveguideCellsAdded += _ => added++;
        var version = grid.WaveguideVersion;

        grid.RestoreWaveguideObstacle(id, Line(50), 4);

        added.ShouldBe(0, "unchanged obstacles must retain their registered raster");
        grid.WaveguideVersion.ShouldBe(version);
        AssertSameCells(grid, WithLine(50, 4));
    }

    [Fact]
    public void ExactRestoration_RepairsFreedCellsWithoutOverwritingOtherOwners()
    {
        var grid = Grid();
        var id = Guid.NewGuid();
        grid.AddWaveguideObstacle(id, Line(50), 4);
        var freed = grid.PhysicalToGrid(30, 50);
        var component = grid.PhysicalToGrid(40, 50);
        grid.SetCellState(freed.gridX, freed.gridY, 0);
        grid.SetCellState(component.gridX, component.gridY, 1);

        grid.RestoreWaveguideObstacle(id, Line(50), 4);

        grid.GetCellState(freed.gridX, freed.gridY).ShouldBe((byte)2);
        grid.GetCellState(component.gridX, component.gridY).ShouldBe((byte)1);
    }

    [Fact]
    public void LiveSegmentMutation_DoesNotCorruptRegisteredShapeComparison()
    {
        var grid = Grid();
        var id = Guid.NewGuid();
        var segments = Line(50);
        grid.AddWaveguideObstacle(id, segments, 4);
        segments[0].StartPoint = (10, 70);
        segments[0].EndPoint = (90, 70);

        grid.RestoreWaveguideObstacle(id, segments, 4);

        AssertSameCells(grid, WithLine(70, 4));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    public void ChangedWidth_RebuildsTheRaster(double width)
    {
        var grid = Grid();
        var id = Guid.NewGuid();
        grid.AddWaveguideObstacle(id, Line(50), 4);

        grid.RestoreWaveguideObstacle(id, Line(50), width);

        AssertSameCells(grid, WithLine(50, width));
    }

    [Theory]
    [InlineData(25, 90)]
    [InlineData(20, -90)]
    public void ChangedBend_RebuildsTheRaster(double radius, double sweep)
    {
        var grid = Grid();
        var id = Guid.NewGuid();
        grid.AddWaveguideObstacle(id, new[] { new BendSegment(50, 50, 20, 0, 90) }, 4);
        var changed = new[] { new BendSegment(50, 50, radius, 0, sweep) };

        grid.RestoreWaveguideObstacle(id, changed, 4);

        var expected = Grid();
        expected.AddWaveguideObstacle(id, changed, 4);
        AssertSameCells(grid, expected);
    }

    private static PathfindingGrid Grid() => new(0, 0, 100, 100, 2);
    private static StraightSegment[] Line(double y) => new[] { new StraightSegment(10, y, 90, y, 0) };
    private static PathfindingGrid WithLine(double y, double width)
    {
        var grid = Grid();
        grid.AddWaveguideObstacle(Guid.NewGuid(), Line(y), width);
        return grid;
    }

    private static void AssertSameCells(PathfindingGrid actual, PathfindingGrid expected)
    {
        for (var x = 0; x < actual.Width; x++)
        for (var y = 0; y < actual.Height; y++)
            actual.GetCellState(x, y).ShouldBe(expected.GetCellState(x, y), $"cell ({x}, {y})");
    }
}
