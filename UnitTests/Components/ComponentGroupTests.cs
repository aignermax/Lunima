using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Unit tests for ComponentGroup hierarchical design functionality.
/// Tests group creation, movement, rotation, and nested groups.
/// </summary>
public class ComponentGroupTests
{
    [Fact]
    public void CreateGroup_WithNoChildren_ShouldHaveZeroBounds()
    {
        // Arrange & Act
        var group = new ComponentGroup("Test Group");

        // Assert
        group.GroupName.ShouldBe("Test Group");
        group.ChildComponents.Count.ShouldBe(0);
        group.InternalPaths.Count.ShouldBe(0);
        group.ExternalPins.Count.ShouldBe(0);
        group.WidthMicrometers.ShouldBe(0);
        group.HeightMicrometers.ShouldBe(0);
    }

    [Fact]
    public void AddChild_ShouldSetParentReference()
    {
        // Arrange
        var group = new ComponentGroup("Parent Group");
        var child = TestComponentFactory.CreateBasicComponent();

        // Act
        group.AddChild(child);

        // Assert
        group.ChildComponents.Count.ShouldBe(1);
        group.ChildComponents[0].ShouldBe(child);
        child.ParentGroup.ShouldBe(group);
    }

    [Fact]
    public void AddChild_WithDuplicateComponent_ShouldThrowException()
    {
        // Arrange
        var group = new ComponentGroup("Test Group");
        var child = TestComponentFactory.CreateBasicComponent();
        group.AddChild(child);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => group.AddChild(child));
    }

    [Fact]
    public void RemoveChild_ShouldClearParentReference()
    {
        // Arrange
        var group = new ComponentGroup("Test Group");
        var child = TestComponentFactory.CreateBasicComponent();
        group.AddChild(child);

        // Act
        var removed = group.RemoveChild(child);

        // Assert
        removed.ShouldBeTrue();
        group.ChildComponents.Count.ShouldBe(0);
        child.ParentGroup.ShouldBeNull();
    }

    [Fact]
    public void AddChild_ShouldUpdateGroupBounds()
    {
        // Arrange
        var group = new ComponentGroup("Test Group");
        var child1 = TestComponentFactory.CreateBasicComponent();
        child1.PhysicalX = 0;
        child1.PhysicalY = 0;
        child1.WidthMicrometers = 100;
        child1.HeightMicrometers = 100;

        var child2 = TestComponentFactory.CreateBasicComponent();
        child2.PhysicalX = 200;
        child2.PhysicalY = 200;
        child2.WidthMicrometers = 100;
        child2.HeightMicrometers = 100;

        // Act
        group.AddChild(child1);
        group.AddChild(child2);

        // Assert
        group.WidthMicrometers.ShouldBe(300); // 0 to 300 (200 + 100)
        group.HeightMicrometers.ShouldBe(300); // 0 to 300 (200 + 100)
    }

    [Fact]
    public void MoveGroup_ShouldTranslateAllChildren()
    {
        // Arrange
        var group = new ComponentGroup("Test Group");
        group.PhysicalX = 0;
        group.PhysicalY = 0;

        var child1 = TestComponentFactory.CreateBasicComponent();
        child1.PhysicalX = 100;
        child1.PhysicalY = 100;

        var child2 = TestComponentFactory.CreateBasicComponent();
        child2.PhysicalX = 200;
        child2.PhysicalY = 200;

        group.AddChild(child1);
        group.AddChild(child2);

        // Act
        group.MoveGroup(50, 75);

        // Assert
        group.PhysicalX.ShouldBe(50);
        group.PhysicalY.ShouldBe(75);
        child1.PhysicalX.ShouldBe(150);
        child1.PhysicalY.ShouldBe(175);
        child2.PhysicalX.ShouldBe(250);
        child2.PhysicalY.ShouldBe(275);
    }

    [Fact]
    public void MoveGroup_ShouldTranslateFrozenPaths()
    {
        // Arrange
        var group = new ComponentGroup("Test Group");

        var frozenPath = new FrozenWaveguidePath
        {
            Path = new RoutedPath()
        };

        var segment = new StraightSegment(100, 100, 200, 200, 0);
        frozenPath.Path.Segments.Add(segment);

        group.AddInternalPath(frozenPath);

        // Act
        group.MoveGroup(50, 75);

        // Assert
        var movedSegment = frozenPath.Path.Segments[0];
        movedSegment.StartPoint.X.ShouldBe(150, tolerance: 0.01);
        movedSegment.StartPoint.Y.ShouldBe(175, tolerance: 0.01);
        movedSegment.EndPoint.X.ShouldBe(250, tolerance: 0.01);
        movedSegment.EndPoint.Y.ShouldBe(275, tolerance: 0.01);
    }

    [Fact]
    public void MoveGroup_WithBendSegment_ShouldTranslateCenterPoint()
    {
        // Arrange
        var group = new ComponentGroup("Test Group");

        var frozenPath = new FrozenWaveguidePath
        {
            Path = new RoutedPath()
        };

        var bendSegment = new BendSegment(150, 150, 50, 0, 90);
        frozenPath.Path.Segments.Add(bendSegment);

        group.AddInternalPath(frozenPath);

        // Act
        group.MoveGroup(50, 75);

        // Assert
        var movedBend = frozenPath.Path.Segments[0] as BendSegment;
        movedBend.ShouldNotBeNull();
        movedBend.Center.X.ShouldBe(200, tolerance: 0.01);
        movedBend.Center.Y.ShouldBe(225, tolerance: 0.01);
    }

    [Fact]
    public void RotateGroupBy90_ShouldRotateChildPositions()
    {
        // Arrange
        var group = new ComponentGroup("Test Group");
        group.PhysicalX = 0;
        group.PhysicalY = 0;

        var child = TestComponentFactory.CreateBasicComponent();
        child.PhysicalX = 100;
        child.PhysicalY = 0;

        group.AddChild(child);

        // Act
        group.RotateGroupBy90CounterClockwise();

        // Assert - After 90° rotation: (100, 0) → (0, 100)
        child.PhysicalX.ShouldBe(0, tolerance: 0.01);
        child.PhysicalY.ShouldBe(100, tolerance: 0.01);
        child.RotationDegrees.ShouldBe(90);
    }

    [Fact]
    public void AddInternalPath_ShouldAddToCollection()
    {
        // Arrange
        var group = new ComponentGroup("Test Group");
        var frozenPath = new FrozenWaveguidePath
        {
            Path = new RoutedPath()
        };

        // Act
        group.AddInternalPath(frozenPath);

        // Assert
        group.InternalPaths.Count.ShouldBe(1);
        group.InternalPaths[0].ShouldBe(frozenPath);
    }

    [Fact]
    public void RemoveInternalPath_ShouldRemoveFromCollection()
    {
        // Arrange
        var group = new ComponentGroup("Test Group");
        var frozenPath = new FrozenWaveguidePath
        {
            Path = new RoutedPath()
        };
        group.AddInternalPath(frozenPath);

        // Act
        var removed = group.RemoveInternalPath(frozenPath);

        // Assert
        removed.ShouldBeTrue();
        group.InternalPaths.Count.ShouldBe(0);
    }

    [Fact]
    public void AddExternalPin_ShouldAddToCollection()
    {
        // Arrange
        var group = new ComponentGroup("Test Group");
        var pin = new GroupPin
        {
            Name = "external_pin_1",
            RelativeX = 100,
            RelativeY = 50,
            AngleDegrees = 0
        };

        // Act
        group.AddExternalPin(pin);

        // Assert
        group.ExternalPins.Count.ShouldBe(1);
        group.ExternalPins[0].ShouldBe(pin);
    }

    [Fact]
    public void NestedGroups_ParentShouldContainChildGroup()
    {
        // Arrange
        var parentGroup = new ComponentGroup("Parent Group");
        var childGroup = new ComponentGroup("Child Group");

        var component1 = TestComponentFactory.CreateBasicComponent();
        var component2 = TestComponentFactory.CreateBasicComponent();

        childGroup.AddChild(component1);
        childGroup.AddChild(component2);

        // Act
        parentGroup.AddChild(childGroup);

        // Assert
        parentGroup.ChildComponents.Count.ShouldBe(1);
        parentGroup.ChildComponents[0].ShouldBe(childGroup);
        childGroup.ParentGroup.ShouldBe(parentGroup);
    }

    [Fact]
    public void GetAllComponentsRecursive_WithNestedGroups_ShouldReturnAll()
    {
        // Arrange
        var parentGroup = new ComponentGroup("Parent Group");
        var childGroup = new ComponentGroup("Child Group");

        var component1 = TestComponentFactory.CreateBasicComponent();
        var component2 = TestComponentFactory.CreateBasicComponent();
        var component3 = TestComponentFactory.CreateBasicComponent();

        childGroup.AddChild(component1);
        childGroup.AddChild(component2);
        parentGroup.AddChild(childGroup);
        parentGroup.AddChild(component3);

        // Act
        var allComponents = parentGroup.GetAllComponentsRecursive();

        // Assert
        allComponents.Count.ShouldBe(4); // childGroup + component1 + component2 + component3
        allComponents.ShouldContain(childGroup);
        allComponents.ShouldContain(component1);
        allComponents.ShouldContain(component2);
        allComponents.ShouldContain(component3);
    }

    [Fact]
    public void MoveGroup_WithNestedGroup_ShouldMoveAllRecursively()
    {
        // Arrange
        var parentGroup = new ComponentGroup("Parent");
        var childGroup = new ComponentGroup("Child");

        childGroup.PhysicalX = 100;
        childGroup.PhysicalY = 100;

        var component = TestComponentFactory.CreateBasicComponent();
        component.PhysicalX = 150;
        component.PhysicalY = 150;

        childGroup.AddChild(component);
        parentGroup.AddChild(childGroup);

        // Act
        parentGroup.MoveGroup(50, 50);

        // Assert
        childGroup.PhysicalX.ShouldBe(150);
        childGroup.PhysicalY.ShouldBe(150);
        component.PhysicalX.ShouldBe(200);
        component.PhysicalY.ShouldBe(200);
    }
}
