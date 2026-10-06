using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// Pins the contract of <see cref="PathfindingGrid.CanReachGoalDirected"/> (issue #1342):
/// the reachability flood gates the A* searches, so a <c>false</c> verdict must be
/// airtight (no path can exist) while <c>true</c> may be a permissive over-approximation.
/// </summary>
public class PathfindingGridReachabilityTests
{
    [Fact]
    public void CanReachGoalDirected_OpenGrid_ReturnsTrue()
    {
        var grid = new PathfindingGrid(0, 0, 200, 200, cellSize: 1.0);

        grid.CanReachGoalDirected(10, 100, 190, 100, goalToleranceCells: 3, useDiagonals: true)
            .ShouldBeTrue();
    }

    [Fact]
    public void CanReachGoalDirected_GoalWalledIn_ReturnsFalse()
    {
        var grid = new PathfindingGrid(0, 0, 200, 200, cellSize: 1.0);
        BlockRectangle(grid, x1: 170, y1: 80, x2: 190, y2: 120);

        grid.CanReachGoalDirected(10, 100, 180, 100, goalToleranceCells: 3, useDiagonals: true)
            .ShouldBeFalse("the goal region is fully enclosed by obstacles");
    }

    [Fact]
    public void CanReachGoalDirected_WallWithGap_ReturnsTrue()
    {
        var grid = new PathfindingGrid(0, 0, 200, 200, cellSize: 1.0);
        // A wall across the whole grid with one gap at the top end.
        for (int y = 0; y < 195; y++)
            grid.SetCellState(100, y, 1);

        grid.CanReachGoalDirected(10, 100, 190, 100, goalToleranceCells: 3, useDiagonals: true)
            .ShouldBeTrue("the flood finds the gap at the end of the wall");
    }

    [Fact]
    public void CanReachGoalDirected_DiagonalCornerCut_MatchesAStarMovement()
    {
        var grid = new PathfindingGrid(0, 0, 200, 200, cellSize: 1.0);
        // Two obstacle columns whose corners touch diagonally: a diagonal step through
        // the touching corners would cut a blocked corner, which the A* forbids — so
        // the flood must not leak through either.
        for (int y = 0; y < 200; y++)
        {
            if (y < 100)
                grid.SetCellState(100, y, 1);
            if (y >= 100)
                grid.SetCellState(101, y, 1);
        }

        grid.CanReachGoalDirected(10, 50, 190, 50, goalToleranceCells: 3, useDiagonals: true)
            .ShouldBeFalse("the diagonal gap between touching corners is not traversable");
    }

    [Fact]
    public void CanReachGoalDirected_GoalWithinToleranceOfStart_ReturnsTrue()
    {
        var grid = new PathfindingGrid(0, 0, 200, 200, cellSize: 1.0);

        grid.CanReachGoalDirected(50, 50, 52, 51, goalToleranceCells: 3, useDiagonals: false)
            .ShouldBeTrue();
    }

    [Fact]
    public void CanReachGoalDirected_WithoutDiagonals_SameVerdictAsWithDiagonals()
    {
        // The corner-cutting rule makes diagonal moves reach exactly the cells a
        // cardinal flood reaches, so the movement model must not change the verdict.
        var grid = new PathfindingGrid(0, 0, 200, 200, cellSize: 1.0);
        for (int y = 0; y < 195; y++)
            grid.SetCellState(100, y, 1);

        grid.CanReachGoalDirected(10, 100, 190, 100, goalToleranceCells: 3, useDiagonals: false)
            .ShouldBeTrue("the gap at the wall's end is reachable cardinally");

        for (int y = 195; y < 200; y++)
            grid.SetCellState(100, y, 1);

        grid.CanReachGoalDirected(10, 100, 190, 100, goalToleranceCells: 3, useDiagonals: false)
            .ShouldBeFalse("a wall spanning the whole grid blocks both movement models");
    }

    private static void BlockRectangle(PathfindingGrid grid, int x1, int y1, int x2, int y2)
    {
        for (int x = x1; x <= x2; x++)
        for (int y = y1; y <= y2; y++)
            grid.SetCellState(x, y, 1);
    }
}
