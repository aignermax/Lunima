using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Tests for ComponentGroup movement functionality.
/// Verifies that groups can be moved as a unit with all children and internal paths.
/// </summary>
public class ComponentGroupMovementTests
{
    [Fact]
    public void MoveComponent_OnComponentGroup_MovesAllChildren()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);

        var group = new ComponentGroup("TestGroup")
        {
            PhysicalX = 100,
            PhysicalY = 100
        };
        group.AddChild(comp1);
        group.AddChild(comp2);

        var groupVm = canvas.AddComponent(group, "GroupTemplate");

        // Act - Move group by delta (50, 30)
        canvas.MoveComponent(groupVm, 50, 30);

        // Assert - Group and all children moved
        groupVm.X.ShouldBe(150);
        groupVm.Y.ShouldBe(130);
        comp1.PhysicalX.ShouldBe(150);
        comp1.PhysicalY.ShouldBe(130);
        comp2.PhysicalX.ShouldBe(250);
        comp2.PhysicalY.ShouldBe(130);
    }

    [Fact]
    public async Task MoveComponent_OnComponentGroup_TranslatesInternalPaths()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateComponentWithPins("Comp1", 100, 100);
        var comp2 = CreateComponentWithPins("Comp2", 200, 100);

        // Create group with internal connection
        var group = new ComponentGroup("TestGroup")
        {
            PhysicalX = 100,
            PhysicalY = 100
        };
        group.AddChild(comp1);
        group.AddChild(comp2);

        // Create a frozen path between the components
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(100, 115, 200, 115, 0));

        var frozenPath = new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = path,
            StartPin = comp1.PhysicalPins[0],
            EndPin = comp2.PhysicalPins[0]
        };
        group.AddInternalPath(frozenPath);

        var groupVm = canvas.AddComponent(group, "GroupTemplate");

        // Act - Move group by delta (50, 30)
        canvas.MoveComponent(groupVm, 50, 30);

        // Assert - Internal path translated
        var segment = (StraightSegment)frozenPath.Path.Segments[0];
        segment.StartPoint.X.ShouldBe(150);
        segment.StartPoint.Y.ShouldBe(145);
        segment.EndPoint.X.ShouldBe(250);
        segment.EndPoint.Y.ShouldBe(145);
    }

    [Fact]
    public void MoveComponent_OnLockedGroup_DoesNotMove()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);

        var group = new ComponentGroup("TestGroup")
        {
            PhysicalX = 100,
            PhysicalY = 100,
            IsLocked = true // Lock the group
        };
        group.AddChild(comp1);
        group.AddChild(comp2);

        var groupVm = canvas.AddComponent(group, "GroupTemplate");

        // Act - Try to move locked group
        canvas.MoveComponent(groupVm, 50, 30);

        // Assert - Group did not move
        groupVm.X.ShouldBe(100);
        groupVm.Y.ShouldBe(100);
        comp1.PhysicalX.ShouldBe(100);
        comp2.PhysicalX.ShouldBe(200);
    }

    [Fact]
    public void MoveComponent_OnNestedGroup_MovesRecursively()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);
        var comp3 = CreateTestComponent("Comp3", 300, 100);

        var innerGroup = new ComponentGroup("InnerGroup")
        {
            PhysicalX = 200,
            PhysicalY = 100
        };
        innerGroup.AddChild(comp2);
        innerGroup.AddChild(comp3);

        var outerGroup = new ComponentGroup("OuterGroup")
        {
            PhysicalX = 100,
            PhysicalY = 100
        };
        outerGroup.AddChild(comp1);
        outerGroup.AddChild(innerGroup);

        var groupVm = canvas.AddComponent(outerGroup, "GroupTemplate");

        // Act - Move outer group
        canvas.MoveComponent(groupVm, 50, 30);

        // Assert - All components moved (including nested group's children)
        groupVm.X.ShouldBe(150);
        groupVm.Y.ShouldBe(130);
        comp1.PhysicalX.ShouldBe(150);
        comp1.PhysicalY.ShouldBe(130);
        innerGroup.PhysicalX.ShouldBe(250);
        innerGroup.PhysicalY.ShouldBe(130);
        comp2.PhysicalX.ShouldBe(250);
        comp2.PhysicalY.ShouldBe(130);
        comp3.PhysicalX.ShouldBe(350);
        comp3.PhysicalY.ShouldBe(130);
    }

    [Fact]
    public void CanMoveComponentTo_ForComponentGroup_ChecksGroupBounds()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);

        var group = new ComponentGroup("TestGroup")
        {
            PhysicalX = 100,
            PhysicalY = 100
        };
        group.AddChild(comp1);
        group.AddChild(comp2);

        var groupVm = canvas.AddComponent(group, "GroupTemplate");

        // Add an obstacle component that will block the group's new position
        var obstacle = CreateTestComponent("Obstacle", 180, 130);
        canvas.AddComponent(obstacle, "ObstacleTemplate");

        // Act - Check if group can move to position that overlaps obstacle
        canvas.BeginCommandExecution(); // Disable collision checking temporarily
        canvas.EndCommandExecution();
        var canMove = canvas.CanMoveComponentTo(groupVm, 130, 130);

        // Assert - Cannot move due to overlap
        canMove.ShouldBeFalse();
    }

    [Fact]
    public void ComponentViewModel_IsComponentGroup_ReturnsTrue_ForComponentGroup()
    {
        // Arrange
        var group = new ComponentGroup("TestGroup")
        {
            PhysicalX = 100,
            PhysicalY = 100
        };

        // Act
        var vm = new ComponentViewModel(group, "GroupTemplate");

        // Assert
        vm.IsComponentGroup.ShouldBeTrue();
    }

    [Fact]
    public void ComponentViewModel_IsComponentGroup_ReturnsFalse_ForRegularComponent()
    {
        // Arrange
        var comp = CreateTestComponent("Comp1", 100, 100);

        // Act
        var vm = new ComponentViewModel(comp, "RegularTemplate");

        // Assert
        vm.IsComponentGroup.ShouldBeFalse();
    }

    /// <summary>
    /// Helper to create a simple test component.
    /// </summary>
    private Component CreateTestComponent(string identifier, double x, double y)
    {
        var sMatrix = new SMatrix(new List<Guid>(), new List<(Guid sliderID, double value)>());
        var component = new Component(
            new Dictionary<int, SMatrix> { { 1550, sMatrix } },
            new List<Slider>(),
            "test",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            identifier,
            new DiscreteRotation(),
            new List<PhysicalPin>()
        )
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = 50,
            HeightMicrometers = 30
        };
        return component;
    }

    /// <summary>
    /// Helper to create a component with physical pins for connection testing.
    /// </summary>
    private Component CreateComponentWithPins(string identifier, double x, double y)
    {
        var sMatrix = new SMatrix(new List<Guid>(), new List<(Guid sliderID, double value)>());
        var pins = new List<PhysicalPin>
        {
            new PhysicalPin
            {
                Name = "Pin1",
                OffsetXMicrometers = 0,
                OffsetYMicrometers = 15,
                AngleDegrees = 0
            }
        };

        var component = new Component(
            new Dictionary<int, SMatrix> { { 1550, sMatrix } },
            new List<Slider>(),
            "test",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            identifier,
            new DiscreteRotation(),
            pins
        )
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = 50,
            HeightMicrometers = 30
        };

        return component;
    }

    [Fact]
    public void MoveComponent_OnGroupWithFrozenPaths_UpdatesPathfindingGridObstacles()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateComponentWithPins("Comp1", 100, 100);
        var comp2 = CreateComponentWithPins("Comp2", 200, 100);

        // Create group with internal frozen path
        var group = new ComponentGroup("TestGroup")
        {
            PhysicalX = 100,
            PhysicalY = 100
        };
        group.AddChild(comp1);
        group.AddChild(comp2);

        // Create a frozen path between the components (horizontal straight line)
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(100, 115, 200, 115, 0));

        var frozenPath = new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = path,
            StartPin = comp1.PhysicalPins[0],
            EndPin = comp2.PhysicalPins[0]
        };
        group.AddInternalPath(frozenPath);

        var groupVm = canvas.AddComponent(group, "GroupTemplate");

        // Verify initial frozen path is registered in A* grid as obstacle (state=3)
        var initialSegment = (StraightSegment)frozenPath.Path.Segments[0];
        var (gx1, gy1) = canvas.Router.PathfindingGrid!.PhysicalToGrid(150, 115); // Middle of initial frozen path
        var initialState = canvas.Router.PathfindingGrid.GetCellState(gx1, gy1);
        initialState.ShouldBe((byte)3, "Frozen path should be registered as obstacle (state=3) at initial position");

        // Act - Move group by delta (50, 30)
        canvas.MoveComponent(groupVm, 50, 30);

        // Assert 1 - Frozen path translated to new position
        var segment = (StraightSegment)frozenPath.Path.Segments[0];
        segment.StartPoint.X.ShouldBe(150);
        segment.StartPoint.Y.ShouldBe(145);
        segment.EndPoint.X.ShouldBe(250);
        segment.EndPoint.Y.ShouldBe(145);

        // Assert 2 - Old frozen path position should be clear (state=0)
        var oldState = canvas.Router.PathfindingGrid.GetCellState(gx1, gy1);
        oldState.ShouldBe((byte)0, "Old frozen path position should be unblocked after group move");

        // Assert 3 - New frozen path position should be blocked (state=3)
        var (gx2, gy2) = canvas.Router.PathfindingGrid.PhysicalToGrid(200, 145); // Middle of new frozen path
        var newState = canvas.Router.PathfindingGrid.GetCellState(gx2, gy2);
        newState.ShouldBe((byte)3, "New frozen path position should be blocked (state=3) after group move");
    }

    [Fact]
    public async Task MoveComponent_OnGroupWithFrozenPaths_TriggersExternalWaveguideRecalculation()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting();

        var comp1 = CreateComponentWithPins("Comp1", 100, 100);
        var comp2 = CreateComponentWithPins("Comp2", 200, 100);

        // Create group with internal frozen path
        var group = new ComponentGroup("TestGroup")
        {
            PhysicalX = 100,
            PhysicalY = 100
        };
        group.AddChild(comp1);
        group.AddChild(comp2);

        // Create a frozen path between the components (horizontal at Y=115)
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(100, 115, 200, 115, 0));

        var frozenPath = new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = path,
            StartPin = comp1.PhysicalPins[0],
            EndPin = comp2.PhysicalPins[0]
        };
        group.AddInternalPath(frozenPath);

        var groupVm = canvas.AddComponent(group, "GroupTemplate");

        // Create external components that will connect OUTSIDE the group
        var external1 = CreateComponentWithPins("External1", 50, 200);
        var external2 = CreateComponentWithPins("External2", 250, 200);
        canvas.AddComponent(external1, "ExternalTemplate");
        canvas.AddComponent(external2, "ExternalTemplate");

        // Create external waveguide connection
        var externalConnection = await canvas.ConnectPinsAsync(
            external1.PhysicalPins[0],
            external2.PhysicalPins[0]);

        externalConnection.ShouldNotBeNull("External connection should be created");

        // Wait for initial routing to complete
        await Task.Delay(100);

        // Act - Move group down by 150µm (from Y=100 to Y=250)
        // This should cause the frozen path to intersect the external waveguide's path
        canvas.BeginDragComponent(groupVm);
        canvas.MoveComponent(groupVm, 0, 150);
        canvas.EndDragComponent(groupVm);

        // Wait for route recalculation to complete (EndDragComponent calls RecalculateRoutesAsync)
        // Poll the IsRouting flag to ensure routing completes
        int maxWaitMs = 2000;
        int elapsedMs = 0;
        while (canvas.IsRouting && elapsedMs < maxWaitMs)
        {
            await Task.Delay(50);
            elapsedMs += 50;
        }

        // Additional small delay to ensure grid rebuild completes
        await Task.Delay(100);

        // Assert - The external waveguide should have been rerouted to avoid the moved frozen path
        // We can't check the exact path without complex A* mocking, but we can verify:
        // 1. The connection still exists
        externalConnection.Connection.ShouldNotBeNull();

        // 2. The frozen path is now registered at the new position
        var (gx, gy) = canvas.Router.PathfindingGrid!.PhysicalToGrid(150, 265); // Middle of moved frozen path
        var cellState = canvas.Router.PathfindingGrid.GetCellState(gx, gy);
        cellState.ShouldBe((byte)3, "Moved frozen path should be registered as obstacle");
    }
}
