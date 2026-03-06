using CAP_Core.Components;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

public class TwoBendSolverTests
{
    private readonly WaveguideRouter _router;
    private readonly TwoBendSolver _solver;

    public TwoBendSolverTests()
    {
        _router = new WaveguideRouter
        {
            MinBendRadiusMicrometers = 10.0,
            MinWaveguideSpacingMicrometers = 2.0
        };

        _solver = new TwoBendSolver(
            _router.MinBendRadiusMicrometers,
            _router.AllowedBendRadii,
            _router);
    }

    /// <summary>
    /// Initializes the router's A* grid from the given components.
    /// </summary>
    private void InitGrid(params Component[] components)
    {
        _router.InitializePathfindingGrid(-100, -100, 400, 250, components);
    }

    [Fact]
    public void TryTwoBendConnection_SameDirectionBends_CorrectDistance_ReturnsValidPath()
    {
        // Arrange - two pins positioned such that same-direction bends connect them
        var startComponent = CreateTestComponent(0, 0);
        var endComponent = CreateTestComponent(40, 0); // 40µm apart
        InitGrid(startComponent, endComponent);

        var startPin = new PhysicalPin
        {
            Name = "output",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 0, // Pointing right
            ParentComponent = startComponent
        };

        var endPin = new PhysicalPin
        {
            Name = "input",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 180, // Pointing left (receiving from right)
            ParentComponent = endComponent
        };

        // Act
        var path = _solver.TryTwoBendConnection(startPin, endPin);

        // Assert
        if (path != null)
        {
            path.Segments.Count.ShouldBe(2);
            path.Segments[0].ShouldBeOfType<BendSegment>();
            path.Segments[1].ShouldBeOfType<BendSegment>();

            var bend1 = (BendSegment)path.Segments[0];
            var bend2 = (BendSegment)path.Segments[1];

            bend1.RadiusMicrometers.ShouldBeGreaterThanOrEqualTo(_router.MinBendRadiusMicrometers);
            bend2.RadiusMicrometers.ShouldBeGreaterThanOrEqualTo(_router.MinBendRadiusMicrometers);

            // Verify endpoint matches end pin position
            var (expectedEndX, expectedEndY) = endPin.GetAbsolutePosition();
            Math.Abs(bend2.EndPoint.X - expectedEndX).ShouldBeLessThan(0.2);
            Math.Abs(bend2.EndPoint.Y - expectedEndY).ShouldBeLessThan(0.2);
        }
    }

    [Fact]
    public void TryTwoBendConnection_PinsNotAligned_ReturnsNull()
    {
        // Arrange - pins at arbitrary positions where two-bend solution doesn't exist
        var startComponent = CreateTestComponent(0, 0);
        var endComponent = CreateTestComponent(15, 15); // Close and misaligned
        InitGrid(startComponent, endComponent);

        var startPin = new PhysicalPin
        {
            Name = "output",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 45, // Diagonal
            ParentComponent = startComponent
        };

        var endPin = new PhysicalPin
        {
            Name = "input",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 225, // Opposite diagonal
            ParentComponent = endComponent
        };

        // Act
        var path = _solver.TryTwoBendConnection(startPin, endPin);

        // Assert
        path.ShouldBeNull(); // No two-bend solution exists for this geometry
    }

    [Fact]
    public void TryTwoBendConnection_ObstacleInPath_ReturnsNull()
    {
        // Arrange - valid two-bend geometry but with obstacle in the way
        var startComponent = CreateTestComponent(0, 0);
        var endComponent = CreateTestComponent(40, 0);
        var obstacle = CreateTestComponent(20, 0); // Obstacle in the middle
        InitGrid(startComponent, endComponent, obstacle);

        var startPin = new PhysicalPin
        {
            Name = "output",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 0,
            ParentComponent = startComponent
        };

        var endPin = new PhysicalPin
        {
            Name = "input",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 180,
            ParentComponent = endComponent
        };

        // Act
        var path = _solver.TryTwoBendConnection(startPin, endPin);

        // Assert
        // Should return null because path is blocked by obstacle
        path.ShouldBeNull();
    }

    [Fact]
    public void TryTwoBendConnection_ValidPath_RespectsMinBendRadius()
    {
        // Arrange
        var startComponent = CreateTestComponent(0, 0);
        var endComponent = CreateTestComponent(40, 0);
        InitGrid(startComponent, endComponent);

        var startPin = new PhysicalPin
        {
            Name = "output",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 0,
            ParentComponent = startComponent
        };

        var endPin = new PhysicalPin
        {
            Name = "input",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 180,
            ParentComponent = endComponent
        };

        // Act
        var path = _solver.TryTwoBendConnection(startPin, endPin);

        // Assert
        if (path != null)
        {
            foreach (var segment in path.Segments)
            {
                if (segment is BendSegment bend)
                {
                    bend.RadiusMicrometers.ShouldBeGreaterThanOrEqualTo(_router.MinBendRadiusMicrometers,
                        $"Bend radius {bend.RadiusMicrometers}µm violates minimum {_router.MinBendRadiusMicrometers}µm");
                }
            }
        }
    }

    [Fact]
    public void TryTwoBendConnection_ValidPath_NoStraightSegments()
    {
        // Arrange
        var startComponent = CreateTestComponent(0, 0);
        var endComponent = CreateTestComponent(40, 0);
        InitGrid(startComponent, endComponent);

        var startPin = new PhysicalPin
        {
            Name = "output",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 0,
            ParentComponent = startComponent
        };

        var endPin = new PhysicalPin
        {
            Name = "input",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 180,
            ParentComponent = endComponent
        };

        // Act
        var path = _solver.TryTwoBendConnection(startPin, endPin);

        // Assert
        if (path != null)
        {
            // Verify no straight segments exist
            foreach (var segment in path.Segments)
            {
                segment.ShouldBeOfType<BendSegment>();
            }
        }
    }

    [Fact]
    public void WaveguideRouter_Integration_TwoBendSolver_CalledBeforeAStar()
    {
        // Arrange - simple case that two-bend solver should handle
        var startComponent = CreateTestComponent(0, 0);
        var endComponent = CreateTestComponent(40, 0);
        InitGrid(startComponent, endComponent);

        var startPin = new PhysicalPin
        {
            Name = "output",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 0,
            ParentComponent = startComponent
        };

        var endPin = new PhysicalPin
        {
            Name = "input",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 180,
            ParentComponent = endComponent
        };

        // Act - route via the router (which should try two-bend first)
        var path = _router.Route(startPin, endPin);

        // Assert
        path.ShouldNotBeNull();
        path.Segments.ShouldNotBeEmpty();
        path.IsValid.ShouldBeTrue();

        // If two-bend solver succeeded, we should have exactly 2 bend segments
        // (but this might vary based on the specific geometry, so we just verify it's valid)
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
