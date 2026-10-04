using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// Tests for the targeted rip-up-and-reroute repair pass (issue #1276): a blocked wire
/// whose failure is <see cref="RoutingFailureReason.Contention"/> gets one bounded local
/// retry — the few routed siblings it crosses are ripped up, the blocked wire routes
/// first, the siblings re-route behind it. The result is kept only when the blocked
/// count strictly decreases without a new crossing; otherwise the previous routes are
/// restored exactly. Endpoint-blocked wires and frozen/manual routes are never touched.
/// </summary>
public class ContentionRepairTests
{
    private const double BendRadius = 10.0;

    /// <summary>Generous enough that a slow CI runner never cuts the textbook repair short.</summary>
    private static readonly TimeSpan UnboundedRepairBudget = TimeSpan.FromMinutes(5);

    [Fact]
    public void ContentionBlockedWire_RipUpAndReroute_BothWiresRoute()
    {
        // Single-file corridor (roof / floor walls): wire A's straight route occupies the
        // corridor; wire B's target pin sits on the corridor roof and is only reachable
        // through it. Greedy order (A first) blocks B; routing B first lets A detour
        // north around the roof and both wires route.
        var aWest = CreateTestComponent(-50, 140, width: 50, height: 40);
        var aEast = CreateTestComponent(460, 140, width: 30, height: 40);
        var bEast = CreateTestComponent(450, 170, width: 30, height: 40);
        var roof = CreateTestComponent(60, 105, width: 280, height: 45);
        var floor = CreateTestComponent(60, 170, width: 280, height: 45);

        var router = CreateRouter(-100, -100, 600, 300, aWest, aEast, bEast, roof, floor);
        var manager = new WaveguideConnectionManager(router)
        {
            UseSequentialRouting = true,
            ContentionRepairTimeBudget = UnboundedRepairBudget,
        };

        var connA = manager.AddConnectionWithCachedRoute(
            CreatePin(aWest, 50, 20, 0), CreatePin(aEast, 0, 20, 180),
            CreateStraightPath(0, 160, 460, 160, blocked: false));
        var connB = manager.AddConnectionWithCachedRoute(
            CreatePin(bEast, 0, 20, 180), CreatePin(roof, 140, 45, 90),
            CreateStraightPath(450, 190, 200, 150, blocked: true));

        connB.IsBlockedFallback.ShouldBeTrue();
        connB.FailureReason.ShouldBe(RoutingFailureReason.Contention,
            "both pin escapes are free — the blockage is wire A in the corridor");
        PathIntersectionDetector.Crosses(connA.RoutedPath!, connB.RoutedPath!).ShouldBeTrue(
            "the fixture's blocked fallback must cross the sibling it conflicts with");

        manager.RepairContentionBlockedWires();

        manager.LastContentionRepairAttemptCount.ShouldBe(1);
        manager.LastContentionRepairAcceptCount.ShouldBe(1,
            "routing B first and re-routing A behind it unblocks both wires");
        connA.IsBlockedFallback.ShouldBeFalse();
        connB.IsBlockedFallback.ShouldBeFalse();
        connA.FailureReason.ShouldBe(RoutingFailureReason.None);
        connB.FailureReason.ShouldBe(RoutingFailureReason.None);
        PathIntersectionDetector.Crosses(connA.RoutedPath!, connB.RoutedPath!).ShouldBeFalse(
            "the accepted repair must not leave a crossing behind");
    }

    [Fact]
    public void ContentionBlockedWire_RepairCannotHelp_RestoresPreviousRoutesExactly()
    {
        // The walls span the full grid height except for one corridor that fits a single
        // wire: whoever routes first wins the corridor, the other wire stays blocked.
        // Swapping the order merely swaps WHICH wire is blocked — no strict gain, so the
        // attempt must be rejected and both routes restored exactly.
        var west1 = CreateTestComponent(-80, 140, width: 50, height: 40);
        var east1 = CreateTestComponent(640, 140, width: 50, height: 40);
        var west2 = CreateTestComponent(-80, 60, width: 50, height: 40);
        var east2 = CreateTestComponent(640, 240, width: 50, height: 40);
        var roof = CreateTestComponent(0, -100, width: 600, height: 250);
        var floor = CreateTestComponent(0, 170, width: 600, height: 180);

        var router = CreateRouter(-100, -100, 700, 350, west1, east1, west2, east2, roof, floor);
        var manager = new WaveguideConnectionManager(router) { UseSequentialRouting = true };

        var wire1 = manager.AddConnectionWithCachedRoute(
            CreatePin(west1, 50, 20, 0), CreatePin(east1, 0, 20, 180),
            CreateStraightPath(-30, 160, 640, 160, blocked: false));
        var wire2 = manager.AddConnectionWithCachedRoute(
            CreatePin(west2, 50, 20, 0), CreatePin(east2, 0, 20, 180),
            CreateStraightPath(-30, 80, 640, 260, blocked: true));

        wire2.FailureReason.ShouldBe(RoutingFailureReason.Contention);
        var wire1Before = wire1.RoutedPath!.DeepCopy();
        var wire2Before = wire2.RoutedPath!.DeepCopy();

        manager.RepairContentionBlockedWires();

        manager.LastContentionRepairAttemptCount.ShouldBe(1,
            "wire 2 crosses wire 1, so a rip-up-and-reroute attempt must run");
        manager.LastContentionRepairAcceptCount.ShouldBe(0,
            "the corridor fits one wire — swapping the order cannot reduce the blocked count");
        wire1.IsBlockedFallback.ShouldBeFalse();
        wire2.IsBlockedFallback.ShouldBeTrue();
        wire2.FailureReason.ShouldBe(RoutingFailureReason.Contention);
        AssertSameGeometry(wire1Before, wire1.RoutedPath!, "wire 1");
        AssertSameGeometry(wire2Before, wire2.RoutedPath!, "wire 2");
    }

    [Fact]
    public void EndpointBlockedWire_IsNeverTouched()
    {
        // The target pin sits inside the blocker component's footprint: no wire ordering
        // can free it, so the repair pass must not even attempt a rip-up.
        var source = CreateTestComponent(0, 200);
        var target = CreateTestComponent(300, 200);
        var blocker = CreateTestComponent(255, 205, width: 55, height: 40);

        var router = CreateRouter(-100, -100, 500, 500, source, target, blocker);
        var manager = new WaveguideConnectionManager(router) { UseSequentialRouting = true };

        var connection = manager.AddConnectionWithCachedRoute(
            CreatePin(source, 50, 25, 0), CreatePin(target, 0, 25, 180),
            CreateStraightPath(50, 225, 300, 225, blocked: true));
        connection.FailureReason.ShouldBe(RoutingFailureReason.EndpointBlocked);
        var pathBefore = connection.RoutedPath;

        manager.RepairContentionBlockedWires();

        manager.LastContentionRepairAttemptCount.ShouldBe(0);
        connection.RoutedPath.ShouldBeSameAs(pathBefore);
        connection.IsBlockedFallback.ShouldBeTrue();
    }

    [Fact]
    public void FrozenContentionWire_IsNeverTouched()
    {
        // A frozen route carries manual edits — the repair pass must leave it alone even
        // when it is blocked by contention.
        var west = CreateTestComponent(-50, 25);
        var east = CreateTestComponent(200, 25);
        var north = CreateTestComponent(75, -50);
        var south = CreateTestComponent(75, 150);

        var router = CreateRouter(-200, -200, 500, 500, west, east, north, south);
        var manager = new WaveguideConnectionManager(router) { UseSequentialRouting = true };

        manager.AddConnectionWithCachedRoute(
            CreatePin(west, 50, 25, 0), CreatePin(east, 0, 25, 180),
            CreateStraightPath(0, 50, 200, 50, blocked: false));
        var frozen = manager.AddConnectionWithCachedRoute(
            CreatePin(north, 25, 50, 90), CreatePin(south, 25, 0, 270),
            CreateStraightPath(100, 0, 100, 150, blocked: true));
        frozen.FailureReason.ShouldBe(RoutingFailureReason.Contention);
        frozen.IsRouteFrozen = true;
        var pathBefore = frozen.RoutedPath;

        manager.RepairContentionBlockedWires();

        manager.LastContentionRepairAttemptCount.ShouldBe(0);
        frozen.RoutedPath.ShouldBeSameAs(pathBefore);
        frozen.IsBlockedFallback.ShouldBeTrue();
    }

    private static void AssertSameGeometry(RoutedPath expected, RoutedPath actual, string label)
    {
        actual.Segments.Count.ShouldBe(expected.Segments.Count, $"{label}: segment count changed");
        for (int i = 0; i < expected.Segments.Count; i++)
        {
            actual.Segments[i].StartPoint.ShouldBe(expected.Segments[i].StartPoint,
                $"{label}: segment {i} start changed");
            actual.Segments[i].EndPoint.ShouldBe(expected.Segments[i].EndPoint,
                $"{label}: segment {i} end changed");
        }
        actual.IsBlockedFallback.ShouldBe(expected.IsBlockedFallback, $"{label}: blocked flag changed");
        actual.FailureReason.ShouldBe(expected.FailureReason, $"{label}: failure reason changed");
    }

    private static RoutedPath CreateStraightPath(
        double startX, double startY, double endX, double endY, bool blocked)
    {
        double headingDegrees = Math.Atan2(endY - startY, endX - startX) * 180.0 / Math.PI;
        var path = new RoutedPath { IsBlockedFallback = blocked };
        path.Segments.Add(new StraightSegment(startX, startY, endX, endY, headingDegrees));
        return path;
    }

    private static PhysicalPin CreatePin(Component comp, double offsetX, double offsetY, double angle) =>
        new()
        {
            Name = angle < 90 || angle > 270 ? "output" : "input",
            OffsetXMicrometers = offsetX,
            OffsetYMicrometers = offsetY,
            AngleDegrees = angle,
            ParentComponent = comp
        };

    private static WaveguideRouter CreateRouter(
        double minX, double minY, double maxX, double maxY, params Component[] components)
    {
        var router = new WaveguideRouter
        {
            MinBendRadiusMicrometers = BendRadius,
            MinWaveguideSpacingMicrometers = 2.0
        };
        router.InitializePathfindingGrid(minX, minY, maxX, maxY, components);
        return router;
    }

    private static Component CreateTestComponent(double x, double y, double width = 50, double height = 50)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "test",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: $"Test_{x}_{y}",
            rotationCounterClock: DiscreteRotation.R0);

        component.WidthMicrometers = width;
        component.HeightMicrometers = height;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }
}
