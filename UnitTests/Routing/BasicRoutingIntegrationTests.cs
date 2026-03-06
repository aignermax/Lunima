using Xunit;
using Shouldly;
using CAP_Core.Routing;
using CAP_Core.Components;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using CAP_Core.Components.ComponentHelpers;

namespace UnitTests.Routing;

/// <summary>
/// Integration tests for basic routing scenarios that should always work.
/// These test the full routing stack: TwoBendSolver → A* → PathSmoother.
///
/// KNOWN ISSUES (2026-03-06):
/// - TwoComponents_VerticallyStacked_ShouldConnect: FAILING
///   Root cause: Terminal connector cannot bridge final gap from Manhattan path end to pin
///   A* stops within GoalTolerance (3 cells = 12µm) but terminal connector needs tighter geometry
///
/// - TwoComponents_WithObstacleInBetween_ShouldRouteAround: FAILING
///   Likely same terminal connector issue
///
/// FIXES APPLIED:
/// ✅ Pin corridor pre-clearing: Create pins before InitializePathfindingGrid
/// ✅ PathSmoother: Process only up to lastTurnIndex (was processing all corners)
/// ✅ GridObstacleManager: Increased corridor dimensions to match WaveguideRouter (30µm × 10µm)
/// ✅ TerminalConnector: Relax forwardDistance constraint when lateralOffset is small
///
/// TODO:
/// - Improve terminal connector geometry solving for A*  goal tolerance gaps
/// - OR reduce A* GoalTolerance (but this makes pathfinding much harder/fail)
/// - OR improve A* to get closer to target while respecting pin escape constraints
/// </summary>
public class BasicRoutingIntegrationTests
{
    private readonly WaveguideRouter _router;

    public BasicRoutingIntegrationTests()
    {
        _router = new WaveguideRouter
        {
            MinBendRadiusMicrometers = 10.0,
            MinWaveguideSpacingMicrometers = 2.0,
            AllowedBendRadii = new List<double> { 5, 10, 20, 50 }
        };
    }

    [Fact]
    public void TwoComponents_VerticallyStacked_ShouldConnect()
    {
        // Arrange: Component on top, another underneath
        // This is the exact scenario the user described
        var topComponent = CreateTestComponent(x: 0, y: 0);
        var bottomComponent = CreateTestComponent(x: 0, y: 100); // 100µm below

        // CRITICAL: Create pins BEFORE initializing grid so CollectPinCorridorCells can see them
        // Top component output (right side, pointing right)
        // Pin OUTSIDE the component boundary (component is 50µm wide, so use offset > 25)
        var startPin = CreatePin(topComponent, offsetX: 30, offsetY: 0, angle: 0, isInput: false);

        // Bottom component input (right side, pointing left to receive from right)
        // Changed from -30 to +30 so end pin is on the RIGHT side
        var endPin = CreatePin(bottomComponent, offsetX: 30, offsetY: 0, angle: 180, isInput: true);

        // NOW initialize grid - CollectPinCorridorCells will see the pins
        InitializeGrid(topComponent, bottomComponent);

        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();

        Console.WriteLine($"[TEST] Routing from ({startX}, {startY}) @ {startPin.GetAbsoluteAngle()}°");
        Console.WriteLine($"[TEST]         to ({endX}, {endY}) @ {endPin.GetAbsoluteAngle() + 180}° entry");

        // Grid diagnostics
        if (_router.PathfindingGrid != null)
        {
            var blockedCount = _router.PathfindingGrid.GetBlockedCellCount();
            var totalCells = _router.PathfindingGrid.Width * _router.PathfindingGrid.Height;
            Console.WriteLine($"[TEST] Grid: {_router.PathfindingGrid.Width}x{_router.PathfindingGrid.Height} cells, {blockedCount}/{totalCells} blocked ({100.0 * blockedCount / totalCells:F1}%)");

            var (gridStartX, gridStartY) = _router.PathfindingGrid.PhysicalToGrid(startX, startY);
            var (gridEndX, gridEndY) = _router.PathfindingGrid.PhysicalToGrid(endX, endY);
            Console.WriteLine($"[TEST] Grid coords: start=({gridStartX}, {gridStartY}), end=({gridEndX}, {gridEndY})");
            Console.WriteLine($"[TEST] Manhattan distance: {Math.Abs(gridEndX - gridStartX) + Math.Abs(gridEndY - gridStartY)} cells");

            // Check if start/end cells are blocked
            bool startBlocked = _router.PathfindingGrid.IsBlocked(gridStartX, gridStartY);
            bool endBlocked = _router.PathfindingGrid.IsBlocked(gridEndX, gridEndY);
            Console.WriteLine($"[TEST] Start cell blocked: {startBlocked}, End cell blocked: {endBlocked}");

            // Check cells around start
            Console.WriteLine($"[TEST] Cells around start ({gridStartX}, {gridStartY}):");
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int checkX = gridStartX + dx;
                    int checkY = gridStartY + dy;
                    if (_router.PathfindingGrid.IsInBounds(checkX, checkY))
                    {
                        bool blocked = _router.PathfindingGrid.IsBlocked(checkX, checkY);
                        Console.Write(blocked ? "X" : ".");
                    }
                }
                Console.WriteLine();
            }
        }

        // Act
        var path = _router.Route(startPin, endPin);

        // Assert
        path.ShouldNotBeNull();
        path.Segments.ShouldNotBeEmpty($"Should find a path between vertically stacked components. Segments: {path.Segments.Count}");
        path.IsInvalidGeometry.ShouldBeFalse("Path should have valid geometry");

        Console.WriteLine($"[TEST] SUCCESS! Path has {path.Segments.Count} segments");
    }

    [Fact]
    public void TwoComponents_SimpleStraightLine_ShouldConnect()
    {
        // Arrange: Simplest possible case - two components perfectly aligned
        var leftComponent = CreateTestComponent(x: 0, y: 0);
        var rightComponent = CreateTestComponent(x: 100, y: 0); // Same Y coordinate

        InitializeGrid(leftComponent, rightComponent);

        // Perfectly aligned pins
        var startPin = CreatePin(leftComponent, offsetX: 25, offsetY: 0, angle: 0, isInput: false);
        var endPin = CreatePin(rightComponent, offsetX: -25, offsetY: 0, angle: 180, isInput: true);

        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();

        Console.WriteLine($"[TEST] Routing straight line from ({startX}, {startY}) @ {startPin.GetAbsoluteAngle()}°");
        Console.WriteLine($"[TEST]                          to ({endX}, {endY}) @ {endPin.GetAbsoluteAngle() + 180}° entry");

        // Act
        var path = _router.Route(startPin, endPin);

        // Assert
        path.ShouldNotBeNull();
        path.Segments.ShouldNotBeEmpty("Should find straight line path");
        path.IsInvalidGeometry.ShouldBeFalse();

        Console.WriteLine($"[TEST] SUCCESS! Found path with {path.Segments.Count} segment(s)");
    }

    [Fact]
    public void TwoComponents_HorizontallyAdjacent_ShouldConnect()
    {
        // Arrange: Two components side by side
        var leftComponent = CreateTestComponent(x: 0, y: 0);
        var rightComponent = CreateTestComponent(x: 120, y: 0); // 120µm to the right

        InitializeGrid(leftComponent, rightComponent);

        var startPin = CreatePin(leftComponent, offsetX: 25, offsetY: 0, angle: 0, isInput: false);
        var endPin = CreatePin(rightComponent, offsetX: -25, offsetY: 0, angle: 180, isInput: true);

        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();

        Console.WriteLine($"[TEST] Routing horizontally from ({startX}, {startY}) @ {startPin.GetAbsoluteAngle()}°");
        Console.WriteLine($"[TEST]                         to ({endX}, {endY}) @ {endPin.GetAbsoluteAngle() + 180}° entry");

        // Act
        var path = _router.Route(startPin, endPin);

        // Assert
        path.ShouldNotBeNull();
        path.Segments.ShouldNotBeEmpty("Should find a path between horizontally adjacent components");
        path.IsInvalidGeometry.ShouldBeFalse();

        Console.WriteLine($"[TEST] SUCCESS! Path has {path.Segments.Count} segments");
    }

    [Fact]
    public void TwoComponents_WithObstacleInBetween_ShouldRouteAround()
    {
        // Arrange: Three components - start, end, and obstacle in middle
        var leftComponent = CreateTestComponent(x: 0, y: 0);
        var obstacleComponent = CreateTestComponent(x: 75, y: 0); // In the middle
        var rightComponent = CreateTestComponent(x: 150, y: 0);

        // Create pins BEFORE grid initialization
        var startPin = CreatePin(leftComponent, offsetX: 25, offsetY: 0, angle: 0, isInput: false);
        var endPin = CreatePin(rightComponent, offsetX: -25, offsetY: 0, angle: 180, isInput: true);

        InitializeGrid(leftComponent, obstacleComponent, rightComponent);

        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();

        Console.WriteLine($"[TEST] Routing around obstacle from ({startX}, {startY})");
        Console.WriteLine($"[TEST]                              to ({endX}, {endY})");

        // Act
        var path = _router.Route(startPin, endPin);

        // Assert
        path.ShouldNotBeNull();
        path.Segments.ShouldNotBeEmpty("Should route around obstacle");
        path.IsInvalidGeometry.ShouldBeFalse();

        Console.WriteLine($"[TEST] SUCCESS! Path has {path.Segments.Count} segments");
    }

    // Helper methods
    private void InitializeGrid(params Component[] components)
    {
        _router.InitializePathfindingGrid(-200, -200, 800, 800, components, cellSize: 4.0);
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
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: new List<PhysicalPin>()  // Pass empty pins list to constructor
        );

        component.WidthMicrometers = 50;
        component.HeightMicrometers = 50;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }

    private PhysicalPin CreatePin(Component component, double offsetX, double offsetY, double angle, bool isInput)
    {
        var pin = new PhysicalPin
        {
            Name = isInput ? "input" : "output",
            OffsetXMicrometers = offsetX,
            OffsetYMicrometers = offsetY,
            AngleDegrees = angle,
            ParentComponent = component
        };

        component.PhysicalPins.Add(pin);  // Add pin to component

        return pin;
    }
}
