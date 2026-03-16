using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Integration tests for ComponentGroup collision detection in DesignCanvasViewModel.
/// Tests that the ViewModel correctly uses GroupCollisionDetector for precise collision detection.
/// </summary>
public class GroupCollisionIntegrationTests
{
    #region Helper Methods

    private static Component CreateComponent(string id, double x, double y, double width = 50, double height = 50)
    {
        var component = new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "test",
            "",
            new Part[1, 1] { { new Part() } },
            0,
            id,
            new DiscreteRotation(),
            new List<PhysicalPin>
            {
                new()
                {
                    Name = "pin0",
                    OffsetXMicrometers = 0,
                    OffsetYMicrometers = 0,
                    AngleDegrees = 0
                }
            })
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = width,
            HeightMicrometers = height
        };

        return component;
    }

    private static ComponentGroup CreateGroup(string name, double x, double y)
    {
        var group = new ComponentGroup(name)
        {
            PhysicalX = x,
            PhysicalY = y
        };
        return group;
    }

    private static ComponentViewModel CreateComponentViewModel(Component component)
    {
        return new ComponentViewModel(component);
    }

    #endregion

    [Fact]
    public void CanMoveComponentTo_RegularComponent_UsesStandardCollisionDetection()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateComponent("comp1", 100, 100, 50, 50);
        var comp2 = CreateComponent("comp2", 200, 100, 50, 50);

        var vm1 = CreateComponentViewModel(comp1);
        var vm2 = CreateComponentViewModel(comp2);
        canvas.Components.Add(vm1);
        canvas.Components.Add(vm2);

        // Act - Try to move comp1 to overlap comp2
        bool canMove = canvas.CanMoveComponentTo(vm1, 200, 100);

        // Assert
        canMove.ShouldBeFalse();
    }

    [Fact]
    public void CanMoveComponentTo_ComponentGroup_UsesGroupCollisionDetection()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();

        var group = CreateGroup("TestGroup", 0, 0);
        var child1 = CreateComponent("child1", 10, 10, 50, 50);
        group.AddChild(child1);

        var obstacle = CreateComponent("obstacle", 300, 300, 50, 50);

        var groupVm = CreateComponentViewModel(group);
        var obstacleVm = CreateComponentViewModel(obstacle);
        canvas.Components.Add(groupVm);
        canvas.Components.Add(obstacleVm);

        // Act - Move group to position where child would collide
        // child1 at (10,10), move group to (290,290), child1 would be at (300,300)
        bool canMove = canvas.CanMoveComponentTo(groupVm, 290, 290);

        // Assert - Should detect collision
        canMove.ShouldBeFalse();
    }

    [Fact]
    public void CanMoveComponentTo_ComponentGroupBoundingBoxOverlapOnly_ShouldAllowPlacement()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();

        // Create group with widely spaced children
        var group = CreateGroup("TestGroup", 0, 0);
        var child1 = CreateComponent("child1", 10, 10, 30, 30);
        var child2 = CreateComponent("child2", 200, 10, 30, 30);
        group.AddChild(child1);
        group.AddChild(child2);

        // Place obstacle between children (within bounding box but not touching children)
        var obstacle = CreateComponent("obstacle", 100, 100, 30, 30);

        var groupVm = CreateComponentViewModel(group);
        var obstacleVm = CreateComponentViewModel(obstacle);
        canvas.Components.Add(groupVm);
        canvas.Components.Add(obstacleVm);

        // Act - Keep group at current position (obstacle is in bounding box but not colliding with children)
        bool canMove = canvas.CanMoveComponentTo(groupVm, 0, 0);

        // Assert - Should allow because children don't collide
        canMove.ShouldBeTrue();
    }

    [Fact]
    public void CanMoveComponentTo_GroupWithFrozenPath_DetectsPathCollision()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();

        var group = CreateGroup("TestGroup", 0, 0);
        var child1 = CreateComponent("child1", 10, 10, 50, 50);
        var child2 = CreateComponent("child2", 100, 10, 50, 50);
        group.AddChild(child1);
        group.AddChild(child2);

        // Create frozen path between children
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(60, 35, 100, 35, 0));
        var frozenPath = new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = path,
            StartPin = child1.PhysicalPins[0],
            EndPin = child2.PhysicalPins[0]
        };
        group.AddInternalPath(frozenPath);

        // Place obstacle where frozen path would pass
        var obstacle = CreateComponent("obstacle", 150, 125, 50, 50);

        var groupVm = CreateComponentViewModel(group);
        var obstacleVm = CreateComponentViewModel(obstacle);
        canvas.Components.Add(groupVm);
        canvas.Components.Add(obstacleVm);

        // Act - Move group so frozen path collides with obstacle
        // Path at (60,35)-(100,35), move group from (0,0) to (90,90)
        // Path would move to (150,125)-(190,125) - collides with obstacle
        bool canMove = canvas.CanMoveComponentTo(groupVm, 90, 90);

        // Assert
        canMove.ShouldBeFalse();
    }

    [Fact]
    public void CanMoveComponentTo_ChipBoundaryCheck_WorksForGroups()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var group = CreateGroup("TestGroup", 100, 100);
        var child1 = CreateComponent("child1", 110, 110, 50, 50);
        group.AddChild(child1);

        var groupVm = CreateComponentViewModel(group);
        canvas.Components.Add(groupVm);

        // Act - Try to move group outside chip boundaries
        // ChipMinX/Y default to large negative, ChipMaxX/Y to large positive
        // Move to a position where group bounding box would be outside
        bool canMoveOutside = canvas.CanMoveComponentTo(groupVm, -10000, -10000);

        // Assert
        canMoveOutside.ShouldBeFalse();
    }

    [Fact]
    public void MoveComponent_ComponentGroup_UpdatesChildPositions()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();

        var group = CreateGroup("TestGroup", 0, 0);
        var child1 = CreateComponent("child1", 10, 10, 50, 50);
        var child2 = CreateComponent("child2", 100, 10, 50, 50);
        group.AddChild(child1);
        group.AddChild(child2);

        var groupVm = CreateComponentViewModel(group);
        canvas.Components.Add(groupVm);

        // Act - Move group by delta (50, 50)
        canvas.MoveComponent(groupVm, 50, 50);

        // Assert
        group.PhysicalX.ShouldBe(50);
        group.PhysicalY.ShouldBe(50);
        child1.PhysicalX.ShouldBe(60); // 10 + 50
        child1.PhysicalY.ShouldBe(60); // 10 + 50
        child2.PhysicalX.ShouldBe(150); // 100 + 50
        child2.PhysicalY.ShouldBe(60); // 10 + 50
    }

    [Fact]
    public void SelectionManager_CanMoveGroup_UsesGroupCollisionForComponentGroups()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();

        var group = CreateGroup("TestGroup", 0, 0);
        var child1 = CreateComponent("child1", 10, 10, 50, 50);
        group.AddChild(child1);

        var obstacle = CreateComponent("obstacle", 100, 100, 50, 50);

        var groupVm = CreateComponentViewModel(group);
        var obstacleVm = CreateComponentViewModel(obstacle);
        canvas.Components.Add(groupVm);
        canvas.Components.Add(obstacleVm);

        canvas.Selection.SelectSingle(groupVm);

        // Act - Try to move selected group so child would collide
        // child1 at (10,10), delta (90,90) would move it to (100,100)
        bool canMove = canvas.Selection.CanMoveGroup(canvas, 90, 90);

        // Assert
        canMove.ShouldBeFalse();
    }

    [Fact]
    public void SelectionManager_CanMoveGroup_MultipleGroupsSelected_ChecksAllChildren()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();

        var group1 = CreateGroup("Group1", 0, 0);
        var child1 = CreateComponent("child1", 10, 10, 50, 50);
        group1.AddChild(child1);

        var group2 = CreateGroup("Group2", 200, 0);
        var child2 = CreateComponent("child2", 210, 10, 50, 50);
        group2.AddChild(child2);

        var obstacle = CreateComponent("obstacle", 300, 100, 50, 50);

        var group1Vm = CreateComponentViewModel(group1);
        var group2Vm = CreateComponentViewModel(group2);
        var obstacleVm = CreateComponentViewModel(obstacle);

        canvas.Components.Add(group1Vm);
        canvas.Components.Add(group2Vm);
        canvas.Components.Add(obstacleVm);

        canvas.Selection.AddToSelection(group1Vm);
        canvas.Selection.AddToSelection(group2Vm);

        // Act - Try to move both groups
        // group2 child2 at (210,10), delta (90,90) would move it to (300,100) - collides with obstacle
        bool canMove = canvas.Selection.CanMoveGroup(canvas, 90, 90);

        // Assert
        canMove.ShouldBeFalse();
    }

    [Fact]
    public void SelectionManager_CanMoveGroup_ExcludesSelectedComponents()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();

        var group1 = CreateGroup("Group1", 0, 0);
        var child1 = CreateComponent("child1", 10, 10, 50, 50);
        group1.AddChild(child1);

        var group2 = CreateGroup("Group2", 200, 0);
        var child2 = CreateComponent("child2", 210, 10, 50, 50);
        group2.AddChild(child2);

        var group1Vm = CreateComponentViewModel(group1);
        var group2Vm = CreateComponentViewModel(group2);

        canvas.Components.Add(group1Vm);
        canvas.Components.Add(group2Vm);

        canvas.Selection.AddToSelection(group1Vm);
        canvas.Selection.AddToSelection(group2Vm);

        // Act - Move both groups together so children would be at same position
        // child1 at (10,10), delta (200,0) would move it to (210,10) - same as child2
        // But child2 is also selected, so should be excluded from collision
        bool canMove = canvas.Selection.CanMoveGroup(canvas, 200, 0);

        // Assert - Should allow because both are selected (move together)
        canMove.ShouldBeTrue();
    }
}
