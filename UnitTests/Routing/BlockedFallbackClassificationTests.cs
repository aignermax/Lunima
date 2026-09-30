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
/// Pins the routing-honesty invariant (issue #1266): EVERY blocked fallback carries a
/// classification (<see cref="RoutingFailureReason.EndpointBlocked"/> or
/// <see cref="RoutingFailureReason.Contention"/>) — the design checks use it to tell the
/// user why a wire is blocked. The routing census found a blocked wire reported as
/// unclassified: only <see cref="WaveguideRouter.Route"/> assigned a reason, while the
/// manager-side sibling-crossing stamp and the cached-route restore produced blocked
/// fallbacks with <see cref="RoutingFailureReason.None"/>.
/// </summary>
public class BlockedFallbackClassificationTests
{
    private const double BendRadius = 10.0;

    [Fact]
    public void RestoredBlockedFallback_SealedPin_ClassifiedEndpointBlocked()
    {
        // The .lun format persists IsBlockedFallback but no reason, so a restored blocked
        // wire must be classified on restore. The target pin (300,225) sits inside the
        // blocker component's footprint — no wire ordering can free it.
        var source = CreateTestComponent(0, 200);
        var target = CreateTestComponent(300, 200);
        var blocker = CreateTestComponent(255, 205, width: 55, height: 40);

        var router = CreateRouter(-100, -100, 500, 500, source, target, blocker);
        var manager = new WaveguideConnectionManager(router) { UseSequentialRouting = true };

        var startPin = CreatePin(source, 50, 25, 0);
        var endPin = CreatePin(target, 0, 25, 180);
        var cachedPath = CreateBlockedStraightPath(50, 225, 300, 225);

        var connection = manager.AddConnectionWithCachedRoute(startPin, endPin, cachedPath);

        connection.IsBlockedFallback.ShouldBeTrue();
        connection.FailureReason.ShouldBe(RoutingFailureReason.EndpointBlocked,
            "the target pin is sealed by the blocker footprint — the restore must classify it");
    }

    [Fact]
    public void RestoredBlockedFallback_FreePins_ClassifiedContention()
    {
        // Same restore, but both pin escape corridors are free: the blockage can only
        // have come from other routed wires.
        var source = CreateTestComponent(0, 200);
        var target = CreateTestComponent(300, 200);

        var router = CreateRouter(-100, -100, 500, 500, source, target);
        var manager = new WaveguideConnectionManager(router) { UseSequentialRouting = true };

        var startPin = CreatePin(source, 50, 25, 0);
        var endPin = CreatePin(target, 0, 25, 180);
        var cachedPath = CreateBlockedStraightPath(50, 225, 300, 225);

        var connection = manager.AddConnectionWithCachedRoute(startPin, endPin, cachedPath);

        connection.IsBlockedFallback.ShouldBeTrue();
        connection.FailureReason.ShouldBe(RoutingFailureReason.Contention,
            "no pin is sealed — the restored blocked wire must be classified as contention");
    }

    [Fact]
    public void UnresolvedSiblingCrossing_StampedAsContention()
    {
        // Two clean cached routes that properly cross: the manager's end-of-pass crossing
        // scan degrades the re-routable side to a blocked fallback. A crossing with a
        // routed sibling is contention by definition.
        var west = CreateTestComponent(-50, 25);
        var east = CreateTestComponent(200, 25);
        var north = CreateTestComponent(75, -50);
        var south = CreateTestComponent(75, 150);

        var router = CreateRouter(-200, -200, 500, 500, west, east, north, south);
        var manager = new WaveguideConnectionManager(router) { UseSequentialRouting = true };

        var horizontal = manager.AddConnectionWithCachedRoute(
            CreatePin(west, 50, 25, 0), CreatePin(east, 0, 25, 180),
            CreateCleanStraightPath(0, 50, 200, 50));
        var vertical = manager.AddConnectionWithCachedRoute(
            CreatePin(north, 25, 50, 90), CreatePin(south, 25, 0, 270),
            CreateCleanStraightPath(100, 0, 100, 150));

        horizontal.IsBlockedFallback.ShouldBeFalse();
        vertical.IsBlockedFallback.ShouldBeFalse();

        manager.MarkUnresolvedSiblingCrossings();

        vertical.IsBlockedFallback.ShouldBeTrue(
            "the later re-routable side of the crossing is degraded to a blocked fallback");
        vertical.FailureReason.ShouldBe(RoutingFailureReason.Contention,
            "a crossing with a routed sibling must not stay unclassified");
        horizontal.IsBlockedFallback.ShouldBeFalse(
            "the earlier side of the crossing keeps its route");
    }

    private static RoutedPath CreateBlockedStraightPath(double startX, double startY, double endX, double endY)
    {
        var path = CreateCleanStraightPath(startX, startY, endX, endY);
        path.IsBlockedFallback = true;
        return path;
    }

    private static RoutedPath CreateCleanStraightPath(double startX, double startY, double endX, double endY)
    {
        double headingDegrees = Math.Atan2(endY - startY, endX - startX) * 180.0 / Math.PI;
        var path = new RoutedPath();
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
            rotationCounterClock: DiscreteRotation.R0
        );

        component.WidthMicrometers = width;
        component.HeightMicrometers = height;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }
}
