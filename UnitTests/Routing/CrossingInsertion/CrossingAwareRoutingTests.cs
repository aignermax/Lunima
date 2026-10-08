using CAP_Core;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.CrossingInsertion;

/// <summary>
/// Crossing-aware routing: where the only way between two pins leads across another
/// waveguide, the router may jump straight across it at a right angle — where a crossing
/// component fits — and reports where. With crossing routing off it stays avoid-only.
/// </summary>
public class CrossingAwareRoutingTests
{
    private const double BendRadius = 10;
    private const double WallY = 300;
    private const double PinX = 400;
    private const double CrossingEdge = 10;
    private const double Clearance = 1;
    private const double Penalty = 2000;

    /// <summary>Blocked corridor width of a routed wire (core plus clearance), as the app registers it.</summary>
    private const double ObstacleWidth = 6;

    /// <summary>Pitch of a wire bundle in the logic examples' routing channels (µm).</summary>
    private const double BundlePitch = 12;
    private static readonly CrossingRouteSettings Crossings = new(CrossingEdge, Clearance, Penalty);

    [Fact]
    public void WallAcrossTheChip_WithoutCrossings_StaysBlocked()
    {
        var (router, start, end) = SceneWithWall(new RoutedPath { Segments = { new StraightSegment(0, WallY, 800, WallY, 0) } });

        var path = router.Route(start, end);

        path.IsBlockedFallback.ShouldBeTrue("the wall spans the whole chip — there is no detour");
    }

    [Fact]
    public void WallAcrossTheChip_WithCrossings_CrossesItOnceAtARightAngle()
    {
        var (router, start, end) = SceneWithWall(new RoutedPath { Segments = { new StraightSegment(0, WallY, 800, WallY, 0) } });
        router.CrossingRouting = Crossings;

        var path = router.Route(start, end);

        path.IsBlockedFallback.ShouldBeFalse("the route may cross the wall");
        var crossing = router.LastPlannedCrossings.ShouldHaveSingleItem();
        crossing.CenterY.ShouldBe(WallY, 1e-6, "the crossing sits on the wall's axis");
        crossing.CenterX.ShouldBe(PinX, router.PathfindingGrid!.CellSizeMicrometers, "straight down from the start pin");
    }

    [Fact]
    public void BundleOfParallelWires_IsCrossedByARowOfCrossings()
    {
        // Three wires at a 12-µm pitch — closer than two crossing footprints, so only a
        // row of adjacent crossings (one per wire) gets through.
        var (router, start, end) = SceneWithWalls(WallY - BundlePitch, WallY, WallY + BundlePitch);
        router.CrossingRouting = Crossings;

        var path = router.Route(start, end);

        path.IsBlockedFallback.ShouldBeFalse();
        router.LastPlannedCrossings.Select(c => c.CenterY)
            .ShouldBe(new[] { WallY - BundlePitch, WallY, WallY + BundlePitch }, "one crossing per wire, in travel order");
        router.LastPlannedCrossings.Select(c => c.CrossedConnection).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public void WeightedSearch_StillCrossesTheBundle_WithTheSamePhysicalChecks()
    {
        var (router, start, end) = SceneWithWalls(WallY - BundlePitch, WallY, WallY + BundlePitch);
        router.CrossingRouting = Crossings with { HeuristicWeight = 1.5 };

        var path = router.Route(start, end);

        path.IsBlockedFallback.ShouldBeFalse("a weighted estimate only orders the search, it never relaxes a crossing rule");
        router.LastPlannedCrossings.Count.ShouldBe(3);
    }

    [Fact]
    public void WallWithAGap_WithCrossings_TakesTheDetourInstead()
    {
        // A 120-µm gap far to the side: the detour is far shorter than the crossing penalty.
        var wall = new RoutedPath
        {
            Segments =
            {
                new StraightSegment(0, WallY, 520, WallY, 0),
                new StraightSegment(640, WallY, 800, WallY, 0),
            },
        };
        var (router, start, end) = SceneWithWall(wall);
        router.CrossingRouting = Crossings;

        var path = router.Route(start, end);

        path.IsBlockedFallback.ShouldBeFalse();
        router.LastPlannedCrossings.ShouldBeEmpty("a detour cheaper than a crossing wins");
    }

    private static (WaveguideRouter Router, PhysicalPin Start, PhysicalPin End) SceneWithWalls(params double[] wallYs)
    {
        var (router, start, end) = SceneWithWall(new RoutedPath());
        foreach (var y in wallYs)
            router.PathfindingGrid!.AddWaveguideObstacle(Guid.NewGuid(), new[] { new StraightSegment(0, y, 800, y, 0) }, ObstacleWidth);
        return (router, start, end);
    }

    private static (WaveguideRouter Router, PhysicalPin Start, PhysicalPin End) SceneWithWall(RoutedPath wall)
    {
        var top = Gate(240, 40, (160, 60, 90));       // pin (400,100) facing south
        var bottom = Gate(240, 500, (160, 0, 270));   // pin (400,500) facing north
        var router = new WaveguideRouter { MinBendRadiusMicrometers = BendRadius };
        router.InitializePathfindingGrid(0, 0, 800, 600, new[] { top, bottom });
        if (wall.Segments.Count > 0)
            router.PathfindingGrid!.AddWaveguideObstacle(Guid.NewGuid(), wall.Segments, ObstacleWidth);
        return (router, top.PhysicalPins[0], bottom.PhysicalPins[0]);
    }

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
