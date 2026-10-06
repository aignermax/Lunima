using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// Acceptance tests for the coarse-grid A* retry (issue #1418): a coarse path that
/// collides on the FINE grid — with a registered sibling's exact geometry — must be
/// rejected, and a wire with no physical route must still degrade to an honest blocked
/// fallback with a failure reason.
/// </summary>
public class CoarseGridRetryTests
{
    private const double BendRadius = 10.0;

    /// <summary>
    /// A registered sibling wall spans the full grid width between the start pin (y=100)
    /// and the south-facing end pin (y=245). The only opening the coarse A* gets is the
    /// pin fan-out hole it legitimately clears next to the end pin — every path through
    /// that hole crosses the sibling's exact geometry. The retry must refuse to mint
    /// that overlap: with the wall registered the attempt returns null; with the wall
    /// lifted the identical attempt succeeds, proving the rejection is caused by the
    /// fine-grid collision gate and not by the scene geometry.
    /// </summary>
    [Fact]
    public void CoarsePath_CrossingRegisteredSibling_IsRejected()
    {
        var start = Gate(0, 74, (320, 26, 0));       // start pin (320,100) facing east
        var end = Gate(240, 245, (160, 0, 270));     // end pin (400,245) facing south
        var components = new Component[] { start, end };
        var router = new WaveguideRouter { MinBendRadiusMicrometers = BendRadius };
        router.InitializePathfindingGrid(0, 0, 800, 400, components);
        var grid = router.PathfindingGrid!;

        var wallId = Guid.NewGuid();
        var wall = new RoutedPath();
        wall.Segments.Add(new StraightSegment(0, 220, 800, 220, 0));
        grid.AddWaveguideObstacle(wallId, wall.Segments, 0.5);

        var startPin = start.PhysicalPins[0];
        var endPin = end.PhysicalPins[0];
        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();
        double startAngle = startPin.GetAbsoluteAngle();
        double endInputAngle = AngleUtilities.NormalizeAngle(endPin.GetAbsoluteAngle() + 180);

        try
        {
            var rejected = router.TryRouteCoarseAStar(
                BendRadius, startX, startY, startAngle, endX, endY, endInputAngle,
                startPin, endPin);
            rejected.ShouldBeNull(
                "every path through the fan-out hole crosses the registered wall — the " +
                "fine-grid collision check must reject it (issue #1418)");

            // Control: lift the wall and the same attempt must find a clean path — the
            // rejection above is the collision gate, not the scene geometry.
            grid.RemoveWaveguideObstacle(wallId);
            var accepted = router.TryRouteCoarseAStar(
                BendRadius, startX, startY, startAngle, endX, endY, endInputAngle,
                startPin, endPin);
            accepted.ShouldNotBeNull(
                "with the colliding wall lifted, the same coarse attempt must succeed");
        }
        finally
        {
            grid.RemoveWaveguideObstacle(wallId);
        }
    }

    /// <summary>
    /// Honesty: a wire whose end pin is sealed deep inside a solid component body (the pin
    /// corridor punches only 3·radius into the body) has no physical route. The coarse
    /// retry must not conjure one — the result stays a blocked fallback with a failure
    /// reason, exactly as before the retry existed.
    /// </summary>
    [Fact]
    public void SealedPin_StillEndsBlocked_WithFailureReason()
    {
        var start = Gate(100, 100, (320, 26, 0));
        // End pin sits 160 µm inside its own 320-wide body, facing west — far beyond the
        // 30 µm pin corridor the router may legitimately clear, so no route can reach it.
        var end = Gate(1000, 100, (160, 26, 180));
        var components = new Component[] { start, end };
        var router = new WaveguideRouter { MinBendRadiusMicrometers = BendRadius };
        router.InitializePathfindingGrid(0, 0, 2000, 400, components);

        var path = router.Route(start.PhysicalPins[0], end.PhysicalPins[0]);

        path.IsBlockedFallback.ShouldBeTrue(
            "a sealed pin has no physical route — the coarse retry must not change that");
        path.FailureReason.ShouldNotBe(RoutingFailureReason.None,
            "a blocked fallback must keep an honest failure reason");
    }

    /// <summary>Box component with the given pins, mirroring the repro scene's gate bodies.</summary>
    private static Component Gate(double x, double y, params (double X, double Y, double Angle)[] pins)
    {
        var physicalPins = pins.Select((p, i) => new PhysicalPin
        {
            Name = $"p{i}",
            OffsetXMicrometers = p.X,
            OffsetYMicrometers = p.Y,
            AngleDegrees = p.Angle,
        }).ToList();
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());
        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "test",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: $"Gate_{x}_{y}",
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: physicalPins);
        component.WidthMicrometers = 320;
        component.HeightMicrometers = 60;
        component.PhysicalX = x;
        component.PhysicalY = y;
        return component;
    }
}
