using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// Pins the A*-routing perf optimizations kept from issue #1342: the reachability
/// gate must degrade an unreachable goal to the same blocked fallback the exhausted
/// searches produced, and the memoized proximity cost must stay bit-exact against a
/// fresh scan across every grid mutation.
/// </summary>
public class AStarRoutingOptimizationTests
{
    [Fact]
    public void Route_GoalSealedBySiblingWaveguide_StillDegradesToBlockedFallback()
    {
        // The end pin is ringed by a registered sibling route: no grid path exists.
        // The reachability gate proves it and the outcome stays the controlled
        // degradation the exhausted searches produce.
        var source = CreateTestComponent(100, 500);
        var target = CreateTestComponent(900, 500);
        var router = CreateRouter(0, 0, 2000, 1200, source, target);
        var grid = router.PathfindingGrid!;
        grid.AddWaveguideObstacle(
            Guid.NewGuid(),
            new PathSegment[]
            {
                new StraightSegment(880, 505, 920, 505, 0),
                new StraightSegment(920, 505, 920, 545, 90),
                new StraightSegment(920, 545, 880, 545, 180),
                new StraightSegment(880, 545, 880, 505, 270),
            },
            waveguideWidth: 8.0);
        var startPin = CreatePin(source, 50, 25, 0);
        var endPin = CreatePin(target, 0, 25, 180);

        var path = router.Route(startPin, endPin);

        path.IsBlockedFallback.ShouldBeTrue(
            "an unreachable goal degrades to the same blocked fallback as before");
    }

    [Fact]
    public void CalculateProximityCost_Memoized_MatchesFreshScanAcrossMutations()
    {
        var grid = new PathfindingGrid(0, 0, 60, 60, cellSize: 1.0);
        var memoized = new RoutingCostCalculator { CellSizeMicrometers = 1.0 };
        var samplePoints = new[] { (5, 5), (20, 20), (30, 31), (45, 10), (59, 59), (0, 0) };

        AssertProximityMatchesFreshScan(grid, memoized, samplePoints);

        var firstRoute = new List<PathSegment>
        {
            new StraightSegment(10, 10, 40, 10, 0)
        };
        grid.AddWaveguideObstacle(Guid.NewGuid(), firstRoute, waveguideWidth: 4.0);
        AssertProximityMatchesFreshScan(grid, memoized, samplePoints);

        var secondId = Guid.NewGuid();
        var secondRoute = new List<PathSegment>
        {
            new StraightSegment(10, 30, 40, 50, 0)
        };
        grid.AddWaveguideObstacle(secondId, secondRoute, waveguideWidth: 4.0);
        AssertProximityMatchesFreshScan(grid, memoized, samplePoints);

        grid.RemoveWaveguideObstacle(secondId);
        AssertProximityMatchesFreshScan(grid, memoized, samplePoints);

        grid.SetCellState(50, 50, 2);
        AssertProximityMatchesFreshScan(grid, memoized, samplePoints);
    }

    /// <summary>
    /// Every sample point must return the memoized calculator's value equal to a
    /// pristine calculator's fresh scan — twice, so the second read is a cache hit.
    /// </summary>
    private static void AssertProximityMatchesFreshScan(
        PathfindingGrid grid, RoutingCostCalculator memoized, (int x, int y)[] samplePoints)
    {
        foreach (var (x, y) in samplePoints)
        {
            double fresh = new RoutingCostCalculator { CellSizeMicrometers = 1.0 }
                .CalculateProximityCost(grid, x, y);
            memoized.CalculateProximityCost(grid, x, y).ShouldBe(fresh,
                $"first read at ({x},{y}) must match a fresh scan");
            memoized.CalculateProximityCost(grid, x, y).ShouldBe(fresh,
                $"cached read at ({x},{y}) must match a fresh scan");
        }
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
            MinBendRadiusMicrometers = 10.0,
            MinWaveguideSpacingMicrometers = 2.0,
            // Force the A* path: the direct styled route is not what this test pins.
            PreferDirectStyledRoutes = false
        };
        router.InitializePathfindingGrid(minX, minY, maxX, maxY, components);
        return router;
    }

    private static Component CreateTestComponent(
        double x, double y, double width = 50, double height = 50)
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

        component.PhysicalX = x;
        component.PhysicalY = y;
        component.WidthMicrometers = width;
        component.HeightMicrometers = height;
        return component;
    }
}
