using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using CAP_Core.Routing.CrossingInsertion.CrossingMove;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.CrossingInsertion.CrossingMove;

/// <summary>
/// <see cref="CrossingAxisMovePlanner"/>: sliding a crossing along one wire patches that
/// wire's end straights and re-routes only the wire across it; a patch that would collapse a
/// straight or does not run along the axis falls back to a re-route.
/// </summary>
public class CrossingAxisMovePlannerTests
{
    private const double Shift = 20;
    private static readonly double Half = CrossingTestCircuit.CrossingEdgeMicrometers / 2.0;

    [Fact]
    public void HorizontalMove_PatchesTheHorizontalPieces_AndReroutesTheVerticalOnes()
    {
        var scene = CrossedWiresScene.Build();

        var plan = CrossingAxisMovePlanner.Plan(scene.Crossing, scene.Pieces, CrossingAxis.Horizontal, Shift);

        plan.Delta.ShouldBe((Shift, 0.0));
        plan.Patched.Select(p => p.Piece).ShouldBe(new[] { scene.West, scene.East }, ignoreOrder: true);
        plan.Rerouted.ShouldBe(new[] { scene.North, scene.South }, ignoreOrder: true);
        var west = plan.Patched.Single(p => p.Piece == scene.West).Path.Segments.ShouldHaveSingleItem();
        west.StartPoint.ShouldBe((50.0, 300.0));
        west.EndPoint.X.ShouldBe(CrossedWiresScene.Center - Half + Shift, 1e-9);
        var east = plan.Patched.Single(p => p.Piece == scene.East).Path.Segments.ShouldHaveSingleItem();
        east.StartPoint.X.ShouldBe(CrossedWiresScene.Center + Half + Shift, 1e-9);
        east.EndPoint.ShouldBe((550.0, 300.0));
    }

    [Fact]
    public void Plan_NeverTouchesTheCurrentRoutes()
    {
        var scene = CrossedWiresScene.Build();
        var before = CrossedWiresScene.Geometry(scene.West);

        CrossingAxisMovePlanner.Plan(scene.Crossing, scene.Pieces, CrossingAxis.Horizontal, Shift);

        CrossedWiresScene.Geometry(scene.West).ShouldBe(before);
    }

    [Fact]
    public void VerticalMove_PatchesTheVerticalPieces()
    {
        var scene = CrossedWiresScene.Build();

        var plan = CrossingAxisMovePlanner.Plan(scene.Crossing, scene.Pieces, CrossingAxis.Vertical, -Shift);

        plan.Patched.Select(p => p.Piece).ShouldBe(new[] { scene.North, scene.South }, ignoreOrder: true);
        plan.Rerouted.ShouldBe(new[] { scene.West, scene.East }, ignoreOrder: true);
        plan.Patched.Single(p => p.Piece == scene.North).Path.Segments[^1].EndPoint.Y
            .ShouldBe(CrossedWiresScene.Center - Half - Shift, 1e-9);
    }

    [Fact]
    public void MoveThatWouldCollapseAnEndStraight_ReroutesThatPiece()
    {
        var scene = CrossedWiresScene.Build();
        double eastLength = 550 - (CrossedWiresScene.Center + Half);

        var plan = CrossingAxisMovePlanner.Plan(scene.Crossing, scene.Pieces, CrossingAxis.Horizontal, eastLength);

        plan.Patched.Select(p => p.Piece).ShouldBe(new[] { scene.West });
        plan.Rerouted.ShouldContain(scene.East, "a straight shorter than the minimum is re-routed, not kept");
    }

    [Fact]
    public void PieceEndingInABendAtThePort_IsRerouted()
    {
        var scene = CrossedWiresScene.Build();
        var bent = new RoutedPath();
        bent.Segments.Add(new StraightSegment(50, 300, 200, 300, 0));
        bent.Segments.Add(new BendSegment(200, 310, 10, -90, 90));
        scene.West.RestoreCachedPath(bent);

        var plan = CrossingAxisMovePlanner.Plan(scene.Crossing, scene.Pieces, CrossingAxis.Horizontal, Shift);

        plan.Rerouted.ShouldContain(scene.West);
    }

    [Fact]
    public void FindWireUnderMovedFootprint_ReportsAThirdWireOnlyWhenTheTargetCoversIt()
    {
        var scene = CrossedWiresScene.Build();
        var third = new WaveguideConnection { StartPin = scene.West.StartPin, EndPin = scene.East.EndPin };
        var route = new RoutedPath();
        route.Segments.Add(new StraightSegment(350, 100, 350, 500, 90));
        third.RestoreCachedPath(route);
        var all = scene.Pieces.Append(third).ToList();

        CrossingAxisMovePlanner.FindWireUnderMovedFootprint(scene.Crossing, (50, 0), all).ShouldBe(third);
        CrossingAxisMovePlanner.FindWireUnderMovedFootprint(scene.Crossing, (Shift, 0), all).ShouldBeNull(
            "the crossing's own pieces never count as obstacles");
    }
}
