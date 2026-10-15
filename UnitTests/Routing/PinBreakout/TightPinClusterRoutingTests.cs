using CAP_Core.Analysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Routing.PinBreakout;

/// <summary>
/// End-to-end breakout routing through the real <see cref="WaveguideConnectionManager"/>
/// pipeline: an MMI-like block whose two input pins sit 8 µm apart (below the router's
/// routable pitch) is fed from two sources. Every wire must route — clean, crossing-free and
/// DRC-lite valid — including the swapped feed, where the wire to the upper pin comes from
/// below and the wire to the lower pin from above, so one of them has to go around.
/// </summary>
public class TightPinClusterRoutingTests
{
    private const double BendRadius = 10.0;
    private const double InputPitch = 8.0;
    private const double MmiX = 400;
    private const double MmiY = 300;
    private const double MmiWidth = 60;
    private const double MmiHeight = 30;
    private const double UpperPinOffsetY = 11;
    private const double SourceWidth = 40;
    private const double SourceHeight = 20;
    private const double ChipSize = 800;

    [Theory]
    [InlineData(false, InputPitch)]
    [InlineData(true, InputPitch)]
    [InlineData(true, 4.0)]
    [InlineData(true, 1.5)]
    public void MmiInputsCloserThanTheRoutablePitch_BothFeedsRoute(bool swappedFeeds, double pitch)
    {
        var scene = Scene.Build(swappedFeeds, pitch);

        scene.Manager.RecalculateAllTransmissions();

        scene.Router.PinBreakoutPlans.Keys.ShouldContain(scene.UpperInput);
        scene.Router.PinBreakoutPlans.Keys.ShouldContain(scene.LowerInput);
        foreach (var wire in scene.Wires)
        {
            wire.IsBlockedFallback.ShouldBeFalse("both wires into the 8 µm input pair must route");
            wire.RoutedPath.ShouldNotBeNull();
            wire.RoutedPath!.IsValid.ShouldBeTrue();
            PathIntersectionDetector.HasSelfIntersection(wire.RoutedPath).ShouldBeFalse();
        }
        PathIntersectionDetector.Crosses(scene.Wires[0].RoutedPath!, scene.Wires[1].RoutedPath!)
            .ShouldBeFalse("the feeds must not cross without a crossing component");
        new DesignValidator().Validate(scene.Wires, Array.Empty<ComponentGroup>())
            .Where(i => i.Type == DesignIssueType.BlockedPath).ShouldBeEmpty();
    }

    [Fact]
    public void BreakoutRoutes_StayRoutedThroughFurtherPasses()
    {
        var scene = Scene.Build(swappedFeeds: true, pitch: 2.0);
        scene.Manager.RecalculateAllTransmissions();

        scene.Manager.RecalculateAllTransmissions();
        scene.Manager.RecalculateAllTransmissions();

        scene.Wires.ShouldAllBe(w => !w.IsBlockedFallback && w.RoutedPath != null && w.RoutedPath.IsValid,
            "breakout routes are valid geometry — later passes must not degrade them");
        PathIntersectionDetector.Crosses(scene.Wires[0].RoutedPath!, scene.Wires[1].RoutedPath!).ShouldBeFalse();
    }

    [Fact]
    public void SwappedFeedAtTwoMicrometers_UsesTheBreakoutGeometry()
    {
        var scene = Scene.Build(swappedFeeds: true, pitch: 2.0);

        scene.Manager.RecalculateAllTransmissions();

        scene.Wires.ShouldContain(w => EndsWithPlannedBreakout(w, scene.Router),
            "at a 2 µm pitch at least one wire must arrive through its planned S-bend");
    }

    private static bool EndsWithPlannedBreakout(WaveguideConnection wire, WaveguideRouter router)
    {
        if (!router.PinBreakoutPlans.TryGetValue(wire.EndPin, out var plan)) return false;
        var first = plan.BreakoutSegments[0];
        var last = wire.RoutedPath!.Segments[^1];
        return Math.Abs(last.EndPoint.X - first.StartPoint.X) < 1e-6
            && Math.Abs(last.StartPoint.X - first.EndPoint.X) < 1e-6
            && Math.Abs(last.StartPoint.Y - first.EndPoint.Y) < 1e-6;
    }

    [Fact]
    public void MmiInputs_RoutesEndAtTheirPinsWithTangentContinuity()
    {
        var scene = Scene.Build(swappedFeeds: true);

        scene.Manager.RecalculateAllTransmissions();

        foreach (var wire in scene.Wires)
        {
            var segments = wire.RoutedPath!.Segments;
            var (endX, endY) = wire.EndPin.GetAbsolutePosition();
            segments[^1].EndPoint.X.ShouldBe(endX, 1e-6);
            segments[^1].EndPoint.Y.ShouldBe(endY, 1e-6);
            for (int i = 1; i < segments.Count; i++)
            {
                double turn = CAP_Core.Routing.AStarPathfinder.AngleUtilities.NormalizeAngle(
                    segments[i].StartAngleDegrees - segments[i - 1].EndAngleDegrees);
                Math.Abs(turn).ShouldBeLessThan(1e-6, $"tangent jump between segments {i - 1} and {i}");
            }
            foreach (var bend in segments.OfType<BendSegment>())
                bend.RadiusMicrometers.ShouldBeGreaterThanOrEqualTo(BendRadius - 1e-9);
        }
    }

    private sealed class Scene
    {
        public required WaveguideRouter Router { get; init; }
        public required WaveguideConnectionManager Manager { get; init; }
        public required List<WaveguideConnection> Wires { get; init; }
        public required PhysicalPin UpperInput { get; init; }
        public required PhysicalPin LowerInput { get; init; }
        public List<Component> Components { get; init; } = new();

        public static Scene Build(bool swappedFeeds, double pitch = InputPitch)
        {
            var mmi = TestComponentFactory.CreatePinlessComponent(MmiX, MmiY, MmiWidth, MmiHeight);
            var upper = AddPin(mmi, 0, UpperPinOffsetY, 180);
            var lower = AddPin(mmi, 0, UpperPinOffsetY + pitch, 180);
            var north = TestComponentFactory.CreatePinlessComponent(100, 100, SourceWidth, SourceHeight);
            var northOut = AddPin(north, SourceWidth, SourceHeight / 2, 0);
            var south = TestComponentFactory.CreatePinlessComponent(100, 500, SourceWidth, SourceHeight);
            var southOut = AddPin(south, SourceWidth, SourceHeight / 2, 0);

            var router = new WaveguideRouter { MinBendRadiusMicrometers = BendRadius };
            router.InitializePathfindingGrid(0, 0, ChipSize, ChipSize, new[] { mmi, north, south });
            var manager = new WaveguideConnectionManager(router) { UseSequentialRouting = true };
            var wires = new List<WaveguideConnection>
            {
                manager.AddConnectionDeferred(northOut, swappedFeeds ? lower : upper),
                manager.AddConnectionDeferred(southOut, swappedFeeds ? upper : lower),
            };
            return new Scene { Router = router, Manager = manager, Wires = wires, UpperInput = upper, LowerInput = lower,
                Components = new List<Component> { mmi, north, south } };
        }

        private static PhysicalPin AddPin(Component component, double x, double y, double angle)
        {
            var pin = TestComponentFactory.CreateRoutingPin(component, x, y, angle);
            component.PhysicalPins.Add(pin);
            return pin;
        }
    }
}
