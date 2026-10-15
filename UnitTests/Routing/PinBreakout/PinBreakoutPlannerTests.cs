using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Routing.PinBreakout;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Routing.PinBreakout;

/// <summary>
/// Breakout planning on a real grid: anchors land one routable pitch apart in the pins'
/// order, turned breakouts follow the far ends, obstructed regions get no plan, and the
/// planned region is reserved on the grid without counting as a component collision.
/// </summary>
public class PinBreakoutPlannerTests
{
    private const double Radius = 10.0;
    private const double CellSize = 4.0;
    private const double BandWidth = 4.0;
    private const double MinSpacing = 2.0;

    [Fact]
    public void RoutablePitch_IsBandPlusGapRoundedUpToCellsPlusOneCell()
    {
        PinBreakoutPlanner.ComputeRoutablePitch(CellSize, BandWidth, MinSpacing).ShouldBe(12.0);
    }

    [Fact]
    public void StraightFan_SpreadsAnchorsToTheRoutablePitch_InPinOrder()
    {
        var (grid, upper, lower, _) = MmiScene();
        var planner = Planner(grid);

        // Far ends straight ahead (west): both wires leave straight.
        var plans = planner.Plan(new[] { Wire(upper, -400, 0), Wire(lower, -400, 8) });

        plans.Count.ShouldBe(2);
        var a = plans[upper];
        var b = plans[lower];
        (b.AnchorY - a.AnchorY).ShouldBe(planner.RoutablePitchMicrometers, 1e-6);
        a.AnchorX.ShouldBe(b.AnchorX, 1e-6);
        a.AnchorHeadingDegrees.ShouldBe(180, 1e-6);
        a.AnchorX.ShouldBeLessThan(a.PinX);
    }

    [Fact]
    public void FarEndsBelow_TurnBothWiresDown_InStaggeredLanes()
    {
        var (grid, upper, lower, _) = MmiScene();
        var planner = Planner(grid);

        // Heading west: the lateral axis points north (−y), so far ends below are the −1 side.
        var plans = planner.Plan(new[] { Wire(upper, -200, 600), Wire(lower, -150, 600) });

        var inner = plans[lower];
        var outer = plans[upper];
        AngleUtilities.NormalizeAngle(inner.AnchorHeadingDegrees - 90).ShouldBe(0, 1e-6);
        AngleUtilities.NormalizeAngle(outer.AnchorHeadingDegrees - 90).ShouldBe(0, 1e-6);
        (inner.AnchorX - outer.AnchorX).ShouldBe(planner.RoutablePitchMicrometers, 1e-6);
    }

    [Fact]
    public void Plans_AreReservedOnTheGrid_AndLiftable()
    {
        var (grid, upper, lower, _) = MmiScene();
        var router = new WaveguideRouter { MinBendRadiusMicrometers = Radius };
        router.InitializePathfindingGrid(grid.MinX, grid.MinY, grid.MaxX, grid.MaxY,
            new[] { upper.ParentComponent });
        router.PlanPinBreakouts(new[] { Wire(upper, -400, 0), Wire(lower, -400, 8) }, BandWidth);
        var routerGrid = router.PathfindingGrid!;
        var plan = router.PinBreakoutPlans[upper];
        var (gx, gy) = routerGrid.PhysicalToGrid(plan.AnchorX, plan.AnchorY);

        routerGrid.IsBreakoutReservedCell(gx, gy).ShouldBeTrue();
        router.IsPathBlockedByComponents(plan.BreakoutSegments).ShouldBeFalse();
        router.IsPathBlocked(plan.LeadSegments).ShouldBeFalse("reservations are no collision for routed geometry");

        var lifted = routerGrid.LiftBreakoutReservations(new[] { plan.ReservationId });
        routerGrid.IsBreakoutReservedCell(gx, gy).ShouldBeFalse();
        routerGrid.RestoreCells(lifted);
        routerGrid.IsBreakoutReservedCell(gx, gy).ShouldBeTrue();

        router.ClearPinBreakouts();
        routerGrid.BreakoutReservationCount.ShouldBe(0);
        routerGrid.IsBreakoutReservedCell(gx, gy).ShouldBeFalse();
    }

    [Fact]
    public void BreakoutIntoAForeignBody_GetsNoPlan()
    {
        var (_, upper, lower, mmi) = MmiScene();
        var wall = TestComponentFactory.CreatePinlessComponent(mmi.PhysicalX - 14, mmi.PhysicalY - 40, 6, 120);
        var grid = new PathfindingGrid(0, 0, 800, 800, CellSize, 5.0);
        grid.RebuildFromComponents(new[] { mmi, wall });

        var plans = Planner(grid).Plan(new[] { Wire(upper, -400, 0), Wire(lower, -400, 8) });

        plans.ShouldBeEmpty();
    }

    [Fact]
    public void CrossingPorts_NeverBreakOut()
    {
        var (grid, upper, lower, mmi) = MmiScene();
        mmi.NazcaFunctionName = CAP_Core.Routing.CrossingInsertion.CrossingComponentCatalog.KnownFunctions[0];

        Planner(grid).Plan(new[] { Wire(upper, -400, 0), Wire(lower, -400, 8) }).ShouldBeEmpty();
    }

    private static PinBreakoutPlanner Planner(PathfindingGrid grid) =>
        new(grid, BandWidth, MinSpacing, new List<double> { 5, 10, 20, 50 });

    /// <summary>A wire from <paramref name="pin"/> to a free-floating far pin at the given offset.</summary>
    private static BreakoutWire Wire(PhysicalPin pin, double dx, double dy)
    {
        var (x, y) = pin.GetAbsolutePosition();
        var far = TestComponentFactory.CreatePinlessComponent(x + dx, y + dy, 1, 1);
        var farPin = TestComponentFactory.CreateRoutingPin(far, 0, 0, 0);
        return new BreakoutWire(farPin, pin, Radius);
    }

    private static (PathfindingGrid Grid, PhysicalPin Upper, PhysicalPin Lower, Component Mmi) MmiScene()
    {
        var mmi = TestComponentFactory.CreatePinlessComponent(400, 300, 60, 30);
        var upper = TestComponentFactory.CreateRoutingPin(mmi, 0, 11, 180);
        var lower = TestComponentFactory.CreateRoutingPin(mmi, 0, 19, 180);
        mmi.PhysicalPins.Add(upper);
        mmi.PhysicalPins.Add(lower);
        var grid = new PathfindingGrid(0, 0, 800, 800, CellSize, 5.0);
        grid.RebuildFromComponents(new[] { mmi });
        return (grid, upper, lower, mmi);
    }
}
