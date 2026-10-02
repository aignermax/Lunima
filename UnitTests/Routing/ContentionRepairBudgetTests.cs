using System.Diagnostics;
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
/// Hardening of the contention rip-up-and-reroute pass (issue #1297): the pass budget is
/// HARD — every attempt routes on a linked token that cancels when the remaining budget
/// expires, so one stubborn in-flight A* search cannot overrun the pass; and an attempt
/// that failed once is not repeated verbatim — its fingerprint (blocked wire, ripped-up
/// siblings, their pin positions) is skipped on later full re-routes until an involved
/// component moves.
/// </summary>
public class ContentionRepairBudgetTests
{
    private const double BendRadius = 10.0;

    [Fact]
    public void BudgetCutAttempt_RestoresPreviousRoutesExactly_OuterTokenStaysUncancelled()
    {
        // Single-corridor fixture: whoever routes first wins the corridor, the other wire
        // stays blocked — a repair attempt always runs and never gains anything.
        var (manager, router, wire1, wire2, _) = CreateSingleCorridorFixture();
        // A zero remaining budget cancels the attempt token before the first route —
        // the same restore path a mid-route budget expiry takes.
        manager.ContentionRepairTimeBudget = TimeSpan.Zero;
        var wire1Before = wire1.RoutedPath!.DeepCopy();
        var wire2Before = wire2.RoutedPath!.DeepCopy();
        using var outerTokens = new CancellationTokenSource();

        bool accepted = manager.TryRepairContentionWire(
            wire2, new List<WaveguideConnection> { wire1 },
            router.PathfindingGrid!, Stopwatch.StartNew(), outerTokens.Token);

        accepted.ShouldBeFalse("a budget-cut attempt can never be accepted");
        outerTokens.IsCancellationRequested.ShouldBeFalse(
            "the budget cancels only the linked attempt token, never the caller's token");
        AssertSameGeometry(wire1Before, wire1.RoutedPath!, "wire 1");
        AssertSameGeometry(wire2Before, wire2.RoutedPath!, "wire 2");
    }

    [Fact]
    public void FailedAttempt_IsSkippedOnSecondRecalculate_AndRetriedAfterInvolvedComponentMoves()
    {
        var (manager, router, wire1, wire2, components) = CreateSingleCorridorFixture();

        manager.RecalculateAllTransmissions();
        manager.LastContentionRepairAttemptCount.ShouldBe(1,
            "the corridor fits one wire — the rip-up-and-reroute attempt runs once and fails");
        manager.LastContentionRepairAcceptCount.ShouldBe(0);
        wire2.IsBlockedFallback.ShouldBeTrue();
        wire1.IsBlockedFallback.ShouldBeFalse();

        manager.RecalculateAllTransmissions();
        manager.LastContentionRepairAttemptCount.ShouldBe(0,
            "an identical attempt already failed and nothing it involves changed — skip it");
        manager.LastContentionRepairAcceptCount.ShouldBe(0);
        wire2.IsBlockedFallback.ShouldBeTrue();

        // Moving a component whose pin positions feed the fingerprint re-enables the attempt.
        var east1 = components.First(c => c.Identifier == "east1");
        east1.PhysicalX -= 40;
        router.PathfindingGrid!.RebuildFromComponents(components);

        manager.RecalculateAllTransmissions();
        manager.LastContentionRepairAttemptCount.ShouldBe(1,
            "an involved component moved — the fingerprint changed, the attempt is retried");
        manager.LastContentionRepairAcceptCount.ShouldBe(0,
            "the corridor still fits only one wire — the retried attempt fails again");
        wire2.IsBlockedFallback.ShouldBeTrue();
    }

    /// <summary>
    /// Two west→east wires and two walls that leave a single one-wire corridor: wire 1
    /// routes through it, wire 2 is contention-blocked, and no wire ordering frees both.
    /// </summary>
    private static (WaveguideConnectionManager Manager, WaveguideRouter Router,
        WaveguideConnection Wire1, WaveguideConnection Wire2, Component[] Components)
        CreateSingleCorridorFixture()
    {
        var west1 = CreateTestComponent("west1", -80, 140, width: 50, height: 40);
        var east1 = CreateTestComponent("east1", 640, 140, width: 50, height: 40);
        var west2 = CreateTestComponent("west2", -80, 60, width: 50, height: 40);
        var east2 = CreateTestComponent("east2", 640, 240, width: 50, height: 40);
        var roof = CreateTestComponent("roof", 0, -100, width: 600, height: 250);
        var floor = CreateTestComponent("floor", 0, 170, width: 600, height: 180);
        var components = new[] { west1, east1, west2, east2, roof, floor };

        var router = CreateRouter(-100, -100, 700, 350, components);
        var manager = new WaveguideConnectionManager(router) { UseSequentialRouting = true };

        var wire1 = manager.AddConnectionWithCachedRoute(
            CreatePin(west1, 50, 20, 0), CreatePin(east1, 0, 20, 180),
            CreateStraightPath(-30, 160, 640, 160, blocked: false));
        var wire2 = manager.AddConnectionWithCachedRoute(
            CreatePin(west2, 50, 20, 0), CreatePin(east2, 0, 20, 180),
            CreateStraightPath(-30, 80, 640, 260, blocked: true));

        wire2.FailureReason.ShouldBe(RoutingFailureReason.Contention);
        return (manager, router, wire1, wire2, components);
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

    private static Component CreateTestComponent(
        string identifier, double x, double y, double width, double height)
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
            identifier: identifier,
            rotationCounterClock: DiscreteRotation.R0);

        component.WidthMicrometers = width;
        component.HeightMicrometers = height;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }
}
