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

        // Create obstacle component in the middle
        var obstacle = CreateTestComponent(70, 0);
        obstacle.WidthMicrometers = 60;
        obstacle.HeightMicrometers = 60;

        // Initialize grid and add obstacle
        router.InitializePathfindingGrid(0, -50, 200, 100, new[] { obstacle });

        // Start and end components
        var startComponent = CreateTestComponent(0, 0);
        var endComponent = CreateTestComponent(150, 0);

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
        path.Segments.Count.ShouldBeGreaterThan(1, "Should have multiple segments to route around obstacle");

        // Path should not go through the obstacle area (70-130, 0-60)
        foreach (var segment in path.Segments)
        {
            if (segment is StraightSegment straight)
            {
                // Check that neither start nor end is inside obstacle
                bool startInObstacle = straight.StartPoint.X >= 70 && straight.StartPoint.X <= 130 &&
                                      straight.StartPoint.Y >= 0 && straight.StartPoint.Y <= 60;
                bool endInObstacle = straight.EndPoint.X >= 70 && straight.EndPoint.X <= 130 &&
                                    straight.EndPoint.Y >= 0 && straight.EndPoint.Y <= 60;

                // Allow some tolerance for boundary cases
                if (straight.StartPoint.X > 75 && straight.StartPoint.X < 125)
                {
                    startInObstacle.ShouldBeFalse($"Segment starts inside obstacle at ({straight.StartPoint.X}, {straight.StartPoint.Y})");
                }
                if (straight.EndPoint.X > 75 && straight.EndPoint.X < 125)
                {
                    endInObstacle.ShouldBeFalse($"Segment ends inside obstacle at ({straight.EndPoint.X}, {straight.EndPoint.Y})");
                }
            }
        }
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
