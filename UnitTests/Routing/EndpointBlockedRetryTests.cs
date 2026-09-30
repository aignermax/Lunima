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
/// Tests for the ordering-retry early stop (issue #1226): when every failed wire of an
/// attempt is endpoint-blocked (a pin sealed in by a component footprint), re-ordering
/// cannot help and the retry storm must stop after the first ordering attempt. Failures
/// caused by other routed wires (contention) must keep the classic retry behaviour.
/// </summary>
public class EndpointBlockedRetryTests
{
    private const double BendRadius = 10.0;

    [Fact]
    public void EndpointBlockedWire_StopsOrderingRetries_AfterFirstAttempt()
    {
        // Blocked pair: the target pin (300,225) sits inside the blocker component's
        // footprint, so no wire ordering can ever free it.
        var source = CreateTestComponent(0, 200);
        var target = CreateTestComponent(300, 200);
        var blocker = CreateTestComponent(255, 205, width: 55, height: 40);
        // Clean pair: a vertical wire that the blocked wire's straight fallback crosses,
        // which forces the full re-route phase with ordering strategies.
        var cleanTop = CreateTestComponent(120, 50);
        var cleanBottom = CreateTestComponent(120, 300);

        var router = CreateRouter(-100, -100, 500, 500, source, target, blocker, cleanTop, cleanBottom);
        var manager = new WaveguideConnectionManager(router) { UseSequentialRouting = true };

        var cleanPinStart = CreatePin(cleanTop, 25, 50, 90);
        var cleanPinEnd = CreatePin(cleanBottom, 25, 0, 270);
        var clean = manager.AddConnection(cleanPinStart, cleanPinEnd);
        clean.IsBlockedFallback.ShouldBeFalse("the clean vertical wire must route on its own");

        var blockedPinStart = CreatePin(source, 50, 25, 0);
        var blockedPinEnd = CreatePin(target, 0, 25, 180);
        var blocked = manager.AddConnection(blockedPinStart, blockedPinEnd);

        blocked.IsBlockedFallback.ShouldBeTrue("the target pin is buried in the blocker footprint");
        blocked.FailureReason.ShouldBe(RoutingFailureReason.EndpointBlocked);
        manager.LastOrderingAttemptCount.ShouldBe(1,
            "all failures are endpoint-blocked — re-ordering cannot free a footprint");
    }

    [Fact]
    public void ContentionFailure_StillRetriesOrderings_AndBothWiresRoute()
    {
        // A single-file corridor (roof / floor walls) is the short lane from west to east.
        // Wire A's straight route passes through it; wire B's target pin qB sits on the
        // corridor roof and is only reachable through the corridor. Routed first, A
        // occupies the corridor and B fails; routed second, A detours north around the
        // roof and both wires route.
        var aWest = CreateTestComponent(-50, 140, width: 50, height: 40);
        var aEast = CreateTestComponent(460, 140, width: 30, height: 40);
        var bEast = CreateTestComponent(450, 170, width: 30, height: 40);
        var roof = CreateTestComponent(60, 105, width: 280, height: 45);
        var floor = CreateTestComponent(60, 170, width: 280, height: 45);

        var router = CreateRouter(-100, -100, 600, 300, aWest, aEast, bEast, roof, floor);
        var manager = new WaveguideConnectionManager(router) { UseSequentialRouting = true };

        var connA = manager.AddConnection(CreatePin(aWest, 50, 20, 0), CreatePin(aEast, 0, 20, 180));
        connA.IsBlockedFallback.ShouldBeFalse("wire A must route on its own");

        var connB = manager.AddConnection(CreatePin(bEast, 0, 20, 180), CreatePin(roof, 140, 45, 90));

        connA.IsBlockedFallback.ShouldBeFalse("wire A must route after the retry");
        connB.IsBlockedFallback.ShouldBeFalse("wire B must route after the retry");
        connA.FailureReason.ShouldBe(RoutingFailureReason.None);
        connB.FailureReason.ShouldBe(RoutingFailureReason.None);
        manager.LastOrderingAttemptCount.ShouldBeGreaterThanOrEqualTo(2,
            "B's failure is contention (A's wire blocks the corridor) — ordering retries must run");
    }

    private static PhysicalPin CreatePin(Component comp, double offsetX, double offsetY, double angle)
    {
        return new PhysicalPin
        {
            Name = angle < 90 || angle > 270 ? "output" : "input",
            OffsetXMicrometers = offsetX,
            OffsetYMicrometers = offsetY,
            AngleDegrees = angle,
            ParentComponent = comp
        };
    }

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
