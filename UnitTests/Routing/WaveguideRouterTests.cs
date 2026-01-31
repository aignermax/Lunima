using CAP_Core.Components;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

public class WaveguideRouterTests
{
    private readonly WaveguideRouter _router;

    public WaveguideRouterTests()
    {
        _router = new WaveguideRouter
        {
            MinBendRadiusMicrometers = 10.0,
            MinWaveguideSpacingMicrometers = 2.0
        };
    }

    [Fact]
    public void Route_StraightAlignment_CreatesStraightSegment()
    {
        // Arrange
        var startComponent = CreateTestComponent(0, 0);
        var endComponent = CreateTestComponent(100, 0);

        var startPin = new PhysicalPin
        {
            Name = "output",
            OffsetXMicrometers = 50,
            OffsetYMicrometers = 25,
            AngleDegrees = 0, // Pointing right
            ParentComponent = startComponent
        };

        var endPin = new PhysicalPin
        {
            Name = "input",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 25,
            AngleDegrees = 180, // Pointing left (receiving from right)
            ParentComponent = endComponent
        };

        // Act
        var path = _router.Route(startPin, endPin);

        // Assert
        path.ShouldNotBeNull();
        path.Segments.ShouldNotBeEmpty();
        path.IsValid.ShouldBeTrue();
        path.TotalLengthMicrometers.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Route_ParallelOffset_CreatesMultipleSegments()
    {
        // Arrange - pins are parallel but offset
        var startComponent = CreateTestComponent(0, 0);
        var endComponent = CreateTestComponent(100, 50);

        var startPin = new PhysicalPin
        {
            Name = "output",
            OffsetXMicrometers = 50,
            OffsetYMicrometers = 25,
            AngleDegrees = 0, // Pointing right
            ParentComponent = startComponent
        };

        var endPin = new PhysicalPin
        {
            Name = "input",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 25,
            AngleDegrees = 180, // Pointing left
            ParentComponent = endComponent
        };

        // Act
        var path = _router.Route(startPin, endPin);

        // Assert
        path.ShouldNotBeNull();
        path.Segments.Count.ShouldBeGreaterThan(1); // Should have bends
        path.TotalEquivalent90DegreeBends.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Route_PerpendicularPins_UsesManhattanRouting()
    {
        // Arrange - pins at 90 degrees
        var startComponent = CreateTestComponent(0, 0);
        var endComponent = CreateTestComponent(50, 50);

        var startPin = new PhysicalPin
        {
            Name = "output",
            OffsetXMicrometers = 25,
            OffsetYMicrometers = 50,
            AngleDegrees = 90, // Pointing up
            ParentComponent = startComponent
        };

        var endPin = new PhysicalPin
        {
            Name = "input",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 25,
            AngleDegrees = 180, // Pointing left
            ParentComponent = endComponent
        };

        // Act
        var path = _router.Route(startPin, endPin);

        // Assert
        path.ShouldNotBeNull();
        path.IsValid.ShouldBeTrue();
        // Manhattan routing with 90-degree turn
        path.TotalEquivalent90DegreeBends.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Route_WithObstacle_UsesAStar()
    {
        // Arrange - set up router with A* grid
        var router = new WaveguideRouter
        {
            MinBendRadiusMicrometers = 10.0,
            MinWaveguideSpacingMicrometers = 2.0,
            Strategy = RoutingStrategy.Auto
        };

        // Components: start at x=0, end at x=200, obstacle in the middle
        // With padding, we need enough room for A* to find a path around the obstacle
        var startComponent = CreateTestComponent(0, 0);
        var endComponent = CreateTestComponent(200, 0);

        // Obstacle positioned so that:
        // 1. It blocks the direct path at y=25 (where pins are)
        // 2. There's enough room (>20µm = 10 cells) before obstacle for A* to turn
        // 3. There's a clear path above or below the obstacle
        var obstacle = CreateTestComponent(100, -50);
        obstacle.WidthMicrometers = 50;
        obstacle.HeightMicrometers = 150; // Blocks y from -50 to 100

        // Initialize grid with generous bounds
        router.InitializePathfindingGrid(-50, -100, 300, 200, new[] { obstacle });

        var grid = router.PathfindingGrid;
        grid.ShouldNotBeNull("Grid should be initialized");
        grid.GetBlockedCellCount().ShouldBeGreaterThan(0, "Grid should have blocked cells from obstacle");

        var startPin = new PhysicalPin
        {
            Name = "output",
            OffsetXMicrometers = 50,
            OffsetYMicrometers = 25,
            AngleDegrees = 0, // Pointing right
            ParentComponent = startComponent
        };

        var endPin = new PhysicalPin
        {
            Name = "input",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 25,
            AngleDegrees = 180, // Pointing left
            ParentComponent = endComponent
        };

        // Act
        var path = router.Route(startPin, endPin);

        // Assert
        path.ShouldNotBeNull();
        path.Segments.ShouldNotBeEmpty("Path should have segments");

        // A* should have found a valid path (not fallback)
        path.IsBlockedFallback.ShouldBeFalse(
            $"A* should find a path. Got {path.Segments.Count} segments, IsBlockedFallback={path.IsBlockedFallback}");

        // When routing around an obstacle, we expect multiple segments (turns)
        path.Segments.Count.ShouldBeGreaterThan(1,
            $"Should have multiple segments to route around obstacle. Got {path.Segments.Count}");
    }

    private Component CreateTestComponent(double x, double y)
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
            rotationCounterClock: DiscreteRotation.R0
        );

        component.WidthMicrometers = 50;
        component.HeightMicrometers = 50;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }
}
