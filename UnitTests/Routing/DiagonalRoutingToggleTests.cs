using Xunit;
using Shouldly;
using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using UnitTests.Helpers;

namespace UnitTests.Routing;

/// <summary>
/// Tests for the diagonal-routing opt-in toggle: with diagonals disabled the
/// search must behave like classic 4-direction routing (Issue #552 follow-up).
/// </summary>
public class DiagonalRoutingToggleTests
{
    private const double CellSize = 1.0;

    /// <summary>Arc radius floor that two arcs cannot meet within the 15 µm forward run of the tight pin pair.</summary>
    private const double ArcFloorMicrometers = 20.0;

    private static (PathfindingGrid grid, RoutingCostCalculator calc) CreateSetup()
    {
        var grid = new PathfindingGrid(0, 0, 100, 100, cellSize: CellSize);
        var calc = new RoutingCostCalculator
        {
            CellSizeMicrometers = CellSize,
            MinStraightRunCells = 4
        };
        return (grid, calc);
    }

    [Fact]
    public void FindPath_DiagonalsDisabled_UsesOnlyCardinalDirections()
    {
        var (grid, calc) = CreateSetup();
        calc.UseDiagonals = false;
        var pathfinder = new AStarPathfinder(grid, calc) { UseDiagonals = false };

        var path = pathfinder.FindPath(10, 10, GridDirection.East, 80, 50, GridDirection.East);

        path.ShouldNotBeNull();
        path.ShouldAllBe(n => !n.Direction.IsDiagonal());
    }

    [Fact]
    public void FindPath_DiagonalsDisabled_StillReachesTheGoal()
    {
        var (grid, calc) = CreateSetup();
        calc.UseDiagonals = false;
        var pathfinder = new AStarPathfinder(grid, calc) { UseDiagonals = false };

        var path = pathfinder.FindPath(10, 10, GridDirection.East, 80, 50, GridDirection.East);

        path.ShouldNotBeNull();
        var last = path[^1];
        last.X.ShouldBeInRange(77, 80);
        last.Y.ShouldBe(50);
    }

    [Fact]
    public void DirectFirst_DiagonalsDisabled_LeavesTiltedStraightsToTheGridRouter()
    {
        var (start, end) = TightParallelPins();
        var withDiagonals = new WaveguideRouter { PreferDirectStyledRoutes = true, UseDiagonalRouting = true, MinBendRadiusMicrometers = ArcFloorMicrometers };
        var direct = withDiagonals.Route(start, end);
        direct.IsDirectStyledRoute.ShouldBeTrue("control: with diagonals on this pin pair gets a direct styled route");
        HasDiagonalStraight(direct).ShouldBeTrue("control: two arcs cannot fit the 15 µm run, so the direct route tilts a straight");

        var manhattanOnly = new WaveguideRouter { PreferDirectStyledRoutes = true, UseDiagonalRouting = false, MinBendRadiusMicrometers = ArcFloorMicrometers };
        manhattanOnly.InitializePathfindingGrid(-200, -200, 600, 400,
            new[] { start.ParentComponent, end.ParentComponent });
        var routed = manhattanOnly.Route(start, end);
        routed.IsDirectStyledRoute.ShouldBeFalse("the tilted direct candidate must be handed to the grid router");
        HasDiagonalStraight(routed).ShouldBeFalse(
            "with diagonals off no direct candidate with a tilted straight may bypass the grid router");
    }

    private static bool HasDiagonalStraight(RoutedPath path) =>
        path.Segments.OfType<StraightSegment>().Any(s =>
            Math.Abs(s.EndPoint.X - s.StartPoint.X) > 1e-6 && Math.Abs(s.EndPoint.Y - s.StartPoint.Y) > 1e-6);

    /// <summary>Parallel pins 100 µm apart with only 15 µm of forward run: too tight for two arcs, so the direct policy reaches for the sine polyline.</summary>
    private static (PhysicalPin Start, PhysicalPin End) TightParallelPins()
    {
        var source = TestComponentFactory.CreateFlatStraightWithPhysicalPins("src");
        source.PhysicalX = 0;
        source.PhysicalY = 0;
        var sink = TestComponentFactory.CreateFlatStraightWithPhysicalPins("sink");
        sink.PhysicalX = 25;
        sink.PhysicalY = 100;
        var end = sink.PhysicalPins.First(p => p.Name == "in");
        return (source.PhysicalPins.First(p => p.Name == "out"), end);
    }

    [Fact]
    public void Router_DiagonalRoutingProperty_DefaultsToEnabledAtLibraryLevel()
    {
        // The application layer opts out by default; the library keeps
        // diagonals on so existing octile behavior stays the default for
        // direct WaveguideRouter consumers (tests, LayoutTestRunner).
        new WaveguideRouter().UseDiagonalRouting.ShouldBeTrue();
    }
}
