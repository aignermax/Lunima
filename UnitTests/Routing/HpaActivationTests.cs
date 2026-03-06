using CAP_Core.Components;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Routing;

/// <summary>
/// Tests for HPA* (Hierarchical Pathfinding A*) activation,
/// DistanceTransform incremental updates, and quality guard fallback.
/// </summary>
public class HpaActivationTests
{
    private readonly ITestOutputHelper _output;
    private const double BendRadius = 10.0;
    private const double CellSize = 2.0;

    public HpaActivationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void BuildHierarchicalGraph_CreatesPortalsAndDistanceTransform()
    {
        var router = CreateRouter(-100, -100, 400, 400);
        router.BuildHierarchicalGraph();

        router.CostCalculator.DistanceTransformGrid.ShouldNotBeNull(
            "DistanceTransform should be set after building hierarchical graph");
    }

    [Fact]
    public void DtIncrementalUpdate_WaveguideCellsReduceDistance()
    {
        var router = CreateRouter(-100, -100, 400, 400);
        router.BuildHierarchicalGraph();

        var dt = router.CostCalculator.DistanceTransformGrid!;
        var grid = router.PathfindingGrid!;

        // Pick a cell that should be far from any waveguide initially
        int testX = grid.Width / 2;
        int testY = grid.Height / 2;
        double initialDist = dt.GetDistanceMicrometers(testX, testY);

        _output.WriteLine($"Initial distance at ({testX},{testY}): {initialDist:F1}µm");
        initialDist.ShouldBe(dt.MaxDistanceMicrometers,
            "Center cell should be at max distance when no waveguides exist");

        // Add a waveguide obstacle near the test cell
        var segments = new List<PathSegment>
        {
            new StraightSegment(
                grid.GridToPhysical(testX - 5, testY).Item1,
                grid.GridToPhysical(testX - 5, testY).Item2,
                grid.GridToPhysical(testX + 5, testY).Item1,
                grid.GridToPhysical(testX + 5, testY).Item2,
                0)
        };
        grid.AddWaveguideObstacle(Guid.NewGuid(), segments, 4.0);

        double afterDist = dt.GetDistanceMicrometers(testX, testY);
        _output.WriteLine($"After waveguide distance at ({testX},{testY}): {afterDist:F1}µm");

        afterDist.ShouldBeLessThan(initialDist,
            "Distance should decrease after adding a waveguide nearby");
    }

    [Fact]
    public void DtRebuild_AfterClearAllWaveguides_DistancesReset()
    {
        var router = CreateRouter(-100, -100, 400, 400);
        router.BuildHierarchicalGraph();

        var dt = router.CostCalculator.DistanceTransformGrid!;
        var grid = router.PathfindingGrid!;

        int testX = grid.Width / 2;
        int testY = grid.Height / 2;

        // Add a waveguide obstacle
        var segments = new List<PathSegment>
        {
            new StraightSegment(
                grid.GridToPhysical(testX, testY).Item1,
                grid.GridToPhysical(testX, testY).Item2,
                grid.GridToPhysical(testX + 10, testY).Item1,
                grid.GridToPhysical(testX + 10, testY).Item2,
                0)
        };
        var id = Guid.NewGuid();
        grid.AddWaveguideObstacle(id, segments, 4.0);

        double withWaveguide = dt.GetDistanceMicrometers(testX, testY);
        withWaveguide.ShouldBeLessThan(dt.MaxDistanceMicrometers);

        // Clear all waveguides — should trigger full DT rebuild
        grid.ClearAllWaveguideObstacles();

        double afterClear = dt.GetDistanceMicrometers(testX, testY);
        _output.WriteLine($"After clear: distance at ({testX},{testY}): {afterClear:F1}µm (was {withWaveguide:F1})");

        afterClear.ShouldBe(dt.MaxDistanceMicrometers,
            "Distance should reset to max after clearing all waveguides");
    }

    [Fact]
    public void ShortRoute_StillWorksWithHpaEnabled()
    {
        // Short route (< 200 cells Manhattan) should use flat A* even with HPA* built
        var startComp = CreateTestComponent(0, 0);
        var endComp = CreateTestComponent(100, 0);
        var router = CreateRouter(-50, -50, 250, 150, startComp, endComp);
        router.BuildHierarchicalGraph();

        var startPin = CreatePin(startComp, 50, 25, 0);
        var endPin = CreatePin(endComp, 0, 25, 180);

        var path = router.Route(startPin, endPin);
        path.ShouldNotBeNull();
        path.Segments.ShouldNotBeEmpty();
        path.IsValid.ShouldBeTrue("Short route should still produce a valid path");

        _output.WriteLine($"Short route: {path.Segments.Count} segments, " +
                          $"length={path.TotalLengthMicrometers:F1}µm, " +
                          $"fallback={path.IsBlockedFallback}");
        path.IsBlockedFallback.ShouldBeFalse("Short route should not fall back to Manhattan");
    }

    [Fact]
    public void LongRoute_FindsPathWithHpa()
    {
        // Long route (> 200 cells = 400µm Manhattan distance) that would exhaust flat A*
        var startComp = CreateTestComponent(0, 0);
        var endComp = CreateTestComponent(800, 0);
        var router = CreateRouter(-100, -100, 1000, 200, startComp, endComp);
        router.BuildHierarchicalGraph();

        var startPin = CreatePin(startComp, 50, 25, 0);
        var endPin = CreatePin(endComp, 0, 25, 180);

        var path = router.Route(startPin, endPin);
        path.ShouldNotBeNull();
        path.Segments.ShouldNotBeEmpty("Long route should have segments");

        _output.WriteLine($"Long route: {path.Segments.Count} segments, " +
                          $"length={path.TotalLengthMicrometers:F1}µm, " +
                          $"fallback={path.IsBlockedFallback}");

        // With HPA* enabled, this should succeed (not fallback)
        path.IsBlockedFallback.ShouldBeFalse(
            "Long route should succeed with HPA* enabled (was falling back before)");
    }

    [Fact]
    public void LongRouteWithOffset_FindsPathWithBends()
    {
        // Long route with Y offset requiring bends
        var startComp = CreateTestComponent(0, 0);
        var endComp = CreateTestComponent(800, 100);
        var router = CreateRouter(-100, -100, 1000, 300, startComp, endComp);
        router.BuildHierarchicalGraph();

        var startPin = CreatePin(startComp, 50, 25, 0);
        var endPin = CreatePin(endComp, 0, 25, 180);

        var path = router.Route(startPin, endPin);
        path.ShouldNotBeNull();
        path.Segments.ShouldNotBeEmpty();

        _output.WriteLine($"Long offset route: {path.Segments.Count} segments, " +
                          $"bends={path.TotalEquivalent90DegreeBends:F1}, " +
                          $"length={path.TotalLengthMicrometers:F1}µm");

        path.IsBlockedFallback.ShouldBeFalse("Long offset route should succeed with HPA*");
        path.TotalEquivalent90DegreeBends.ShouldBeGreaterThan(0,
            "Long offset route should include bends");
    }

    [Fact]
    public void SequentialRouting_DtStaysFresh()
    {
        // Route two connections sequentially, verify second route is valid
        var comp1 = CreateTestComponent(0, 0);
        var comp2 = CreateTestComponent(200, 0);

        var router = WaveguideConnection.SharedRouter;
        router.MinBendRadiusMicrometers = BendRadius;
        router.MinWaveguideSpacingMicrometers = 2.0;
        router.InitializePathfindingGrid(-50, -50, 350, 150, new[] { comp1, comp2 });
        router.BuildHierarchicalGraph();

        try
        {
            var pin1Start = CreatePin(comp1, 50, 15, 0);
            var pin1End = CreatePin(comp2, 0, 15, 180);
            var pin2Start = CreatePin(comp1, 50, 35, 0);
            var pin2End = CreatePin(comp2, 0, 35, 180);

            var connManager = new WaveguideConnectionManager { UseSequentialRouting = true };
            var conn1 = connManager.AddConnection(pin1Start, pin1End);
            var conn2 = connManager.AddConnection(pin2Start, pin2End);

            conn1.RoutedPath.ShouldNotBeNull("Connection 1 should have a route");
            conn2.RoutedPath.ShouldNotBeNull("Connection 2 should have a route");

            _output.WriteLine($"Conn1: {conn1.RoutedPath.Segments.Count} segs, " +
                              $"valid={conn1.RoutedPath.IsValid}");
            _output.WriteLine($"Conn2: {conn2.RoutedPath.Segments.Count} segs, " +
                              $"valid={conn2.RoutedPath.IsValid}");

            conn1.RoutedPath.IsValid.ShouldBeTrue("Connection 1 should be valid");
            conn2.RoutedPath.IsValid.ShouldBeTrue("Connection 2 should be valid");
        }
        finally
        {
            router.MinBendRadiusMicrometers = 10.0;
            router.MinWaveguideSpacingMicrometers = 2.0;
            router.InitializePathfindingGrid(-100, -100, 400, 250, Array.Empty<Component>());
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────

    private static WaveguideRouter CreateRouter(
        double minX, double minY, double maxX, double maxY,
        params Component[] components)
    {
        var router = new WaveguideRouter
        {
            MinBendRadiusMicrometers = BendRadius,
            MinWaveguideSpacingMicrometers = 2.0,
            AStarCellSize = CellSize,
            UseHierarchicalPathfinding = true  // Enable HPA* for these tests
        };
        router.InitializePathfindingGrid(minX, minY, maxX, maxY, components);
        return router;
    }

    private static Component CreateTestComponent(double x, double y)
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
            identifier: $"TestComponent_{x}_{y}",
            rotationCounterClock: DiscreteRotation.R0);

        component.WidthMicrometers = 50;
        component.HeightMicrometers = 50;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }

    private static PhysicalPin CreatePin(
        Component parent, double offsetX, double offsetY, double angle)
    {
        return new PhysicalPin
        {
            Name = $"pin_{offsetX}_{offsetY}",
            OffsetXMicrometers = offsetX,
            OffsetYMicrometers = offsetY,
            AngleDegrees = angle,
            ParentComponent = parent
        };
    }
}
