using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.ViewModels;

/// <summary>
/// Unit tests for group edit mode functionality in DesignCanvasViewModel.
/// </summary>
public class GroupEditModeTests
{
    [Fact]
    public void EnterGroupEditMode_SetsCurrentEditGroup()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var group = new ComponentGroup("TestGroup");
        canvas.AddComponent(group);

        // Act
        canvas.EnterGroupEditMode(group);

        // Assert
        canvas.CurrentEditGroup.ShouldBe(group);
        canvas.IsInGroupEditMode.ShouldBeTrue();
    }

    [Fact]
    public void ExitGroupEditMode_ClearsCurrentEditGroup()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var group = new ComponentGroup("TestGroup");
        canvas.AddComponent(group);
        canvas.EnterGroupEditMode(group);

        // Act
        canvas.ExitGroupEditMode();

        // Assert
        canvas.CurrentEditGroup.ShouldBeNull();
        canvas.IsInGroupEditMode.ShouldBeFalse();
    }

    [Fact]
    public void ExitToRoot_ClearsNestedEditStack()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var parentGroup = new ComponentGroup("Parent");
        var childGroup = new ComponentGroup("Child");
        parentGroup.AddChild(childGroup);
        canvas.AddComponent(parentGroup);

        canvas.EnterGroupEditMode(parentGroup);
        canvas.EnterGroupEditMode(childGroup);

        // Act
        canvas.ExitToRoot();

        // Assert
        canvas.CurrentEditGroup.ShouldBeNull();
        canvas.IsInGroupEditMode.ShouldBeFalse();
        canvas.BreadcrumbPath.Count.ShouldBe(0);
    }

    [Fact]
    public void EnterGroupEditMode_UpdatesBreadcrumbPath()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var parentGroup = new ComponentGroup("Parent");
        var childGroup = new ComponentGroup("Child");
        parentGroup.AddChild(childGroup);
        canvas.AddComponent(parentGroup);

        // Act
        canvas.EnterGroupEditMode(parentGroup);

        // Assert
        canvas.BreadcrumbPath.Count.ShouldBe(1);
        canvas.BreadcrumbPath[0].ShouldBe(parentGroup);
    }

    [Fact]
    public void EnterGroupEditMode_NestedGroup_UpdatesBreadcrumbPath()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var parentGroup = new ComponentGroup("Parent");
        var childGroup = new ComponentGroup("Child");
        parentGroup.AddChild(childGroup);
        canvas.AddComponent(parentGroup);

        // Act
        canvas.EnterGroupEditMode(parentGroup);
        canvas.EnterGroupEditMode(childGroup);

        // Assert
        canvas.BreadcrumbPath.Count.ShouldBe(2);
        canvas.BreadcrumbPath[0].ShouldBe(parentGroup);
        canvas.BreadcrumbPath[1].ShouldBe(childGroup);
    }

    [Fact]
    public void NavigateToBreadcrumbLevel_JumpsToSpecificLevel()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var level1 = new ComponentGroup("Level1");
        var level2 = new ComponentGroup("Level2");
        var level3 = new ComponentGroup("Level3");
        level1.AddChild(level2);
        level2.AddChild(level3);
        canvas.AddComponent(level1);

        canvas.EnterGroupEditMode(level1);
        canvas.EnterGroupEditMode(level2);
        canvas.EnterGroupEditMode(level3);

        // Act - Navigate back to level2
        canvas.NavigateToBreadcrumbLevel(level2);

        // Assert
        canvas.CurrentEditGroup.ShouldBe(level2);
        canvas.BreadcrumbPath.Count.ShouldBe(2);
    }

    [Fact]
    public void NavigateToBreadcrumbLevel_WithNull_ExitsToRoot()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var group = new ComponentGroup("TestGroup");
        canvas.AddComponent(group);
        canvas.EnterGroupEditMode(group);

        // Act
        canvas.NavigateToBreadcrumbLevel(null);

        // Assert
        canvas.CurrentEditGroup.ShouldBeNull();
        canvas.IsInGroupEditMode.ShouldBeFalse();
    }

    [Fact]
    public void HierarchyPanel_SyncEditModeFromCanvas_HighlightsEditedGroup()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);
        var group = new ComponentGroup("TestGroup");
        canvas.AddComponent(group);
        hierarchy.RebuildTree();

        // Act
        hierarchy.SyncEditModeFromCanvas(group);

        // Assert
        var groupNode = hierarchy.RootNodes[0];
        groupNode.IsInEditMode.ShouldBeTrue();
    }

    [Fact]
    public void HierarchyPanel_SyncEditModeFromCanvas_WithNull_ClearsAllFlags()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);
        var group = new ComponentGroup("TestGroup");
        canvas.AddComponent(group);
        hierarchy.RebuildTree();
        hierarchy.SyncEditModeFromCanvas(group);

        // Act
        hierarchy.SyncEditModeFromCanvas(null);

        // Assert
        var groupNode = hierarchy.RootNodes[0];
        groupNode.IsInEditMode.ShouldBeFalse();
    }

    [Fact]
    public void EnterGroupEditMode_ThrowsOnNullGroup()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => canvas.EnterGroupEditMode(null!));
    }

    [Fact]
    public void HitTestComponent_InEditMode_ReturnsChildComponent()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var child1 = TestComponentFactory.CreateStraightWaveGuide();
        child1.PhysicalX = 100;
        child1.PhysicalY = 100;
        child1.WidthMicrometers = 50;
        child1.HeightMicrometers = 50;

        var child2 = TestComponentFactory.CreateStraightWaveGuide();
        child2.PhysicalX = 200;
        child2.PhysicalY = 200;
        child2.WidthMicrometers = 50;
        child2.HeightMicrometers = 50;

        var group = new ComponentGroup("TestGroup");
        group.AddChild(child1);
        group.AddChild(child2);
        canvas.AddComponent(group);

        canvas.EnterGroupEditMode(group);

        // Act - Hit test a point inside child1
        var hitPoint = new Avalonia.Point(125, 125);
        var hitComponent = CAP.Avalonia.Controls.DesignCanvasHitTesting.HitTestComponent(hitPoint, canvas);

        // Assert
        hitComponent.ShouldNotBeNull();
        hitComponent.Component.ShouldBe(child1);
    }

    [Fact]
    public void HitTestComponent_InEditMode_IgnoresExternalComponents()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var internalChild = TestComponentFactory.CreateStraightWaveGuide();
        internalChild.PhysicalX = 100;
        internalChild.PhysicalY = 100;
        internalChild.WidthMicrometers = 50;
        internalChild.HeightMicrometers = 50;

        var externalComponent = TestComponentFactory.CreateStraightWaveGuide();
        externalComponent.PhysicalX = 100;
        externalComponent.PhysicalY = 100;
        externalComponent.WidthMicrometers = 50;
        externalComponent.HeightMicrometers = 50;

        var group = new ComponentGroup("TestGroup");
        group.AddChild(internalChild);
        canvas.AddComponent(group);
        canvas.AddComponent(externalComponent);

        canvas.EnterGroupEditMode(group);

        // Act - Hit test the same point (overlapping components)
        var hitPoint = new Avalonia.Point(125, 125);
        var hitComponent = CAP.Avalonia.Controls.DesignCanvasHitTesting.HitTestComponent(hitPoint, canvas);

        // Assert - Should hit internal child, not external component
        hitComponent.ShouldNotBeNull();
        hitComponent.Component.ShouldBe(internalChild);
    }

    [Fact]
    public void HitTestPin_InEditMode_ReturnsChildComponentPins()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var child = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        child.PhysicalX = 100;
        child.PhysicalY = 100;

        var group = new ComponentGroup("TestGroup");
        group.AddChild(child);
        canvas.AddComponent(group);

        canvas.EnterGroupEditMode(group);

        // Act - Hit test near a pin of the child component
        var pinPos = child.PhysicalPins[0].GetAbsolutePosition();
        var hitPoint = new Avalonia.Point(pinPos.Item1, pinPos.Item2);
        var hitPin = CAP.Avalonia.Controls.DesignCanvasHitTesting.HitTestPin(hitPoint, canvas);

        // Assert
        hitPin.ShouldNotBeNull();
        hitPin.ParentComponent.ShouldBe(child);
    }

    [Fact]
    public void HitTestPin_InEditMode_IgnoresExternalComponentPins()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var internalChild = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        internalChild.PhysicalX = 100;
        internalChild.PhysicalY = 100;

        var externalComponent = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        externalComponent.PhysicalX = 500;
        externalComponent.PhysicalY = 500;

        var group = new ComponentGroup("TestGroup");
        group.AddChild(internalChild);
        canvas.AddComponent(group);
        canvas.AddComponent(externalComponent);

        canvas.EnterGroupEditMode(group);

        // Act - Hit test near an external component pin
        var externalPinPos = externalComponent.PhysicalPins[0].GetAbsolutePosition();
        var hitPoint = new Avalonia.Point(externalPinPos.Item1, externalPinPos.Item2);
        var hitPin = CAP.Avalonia.Controls.DesignCanvasHitTesting.HitTestPin(hitPoint, canvas);

        // Assert - Should not hit external pins in edit mode
        hitPin.ShouldBeNull();
    }

    [Fact]
    public void HitTestComponent_NotInEditMode_ReturnsTopLevelComponents()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var child = TestComponentFactory.CreateStraightWaveGuide();
        child.PhysicalX = 100;
        child.PhysicalY = 100;
        child.WidthMicrometers = 50;
        child.HeightMicrometers = 50;

        var group = new ComponentGroup("TestGroup");
        group.AddChild(child);
        canvas.AddComponent(group);

        // NOT in edit mode

        // Act - Hit test a point inside the child
        var hitPoint = new Avalonia.Point(125, 125);
        var hitComponent = CAP.Avalonia.Controls.DesignCanvasHitTesting.HitTestComponent(hitPoint, canvas);

        // Assert - Should return the group, not the child
        hitComponent.ShouldNotBeNull();
        hitComponent.Component.ShouldBe(group);
    }

    [Fact]
    public void ESC_ExitsGroupEditMode_WhenInEditMode()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var group = new ComponentGroup("TestGroup");
        canvas.AddComponent(group);
        canvas.EnterGroupEditMode(group);
        canvas.IsInGroupEditMode.ShouldBeTrue();

        // Act - Simulate ESC key behavior
        canvas.ExitGroupEditMode();

        // Assert
        canvas.IsInGroupEditMode.ShouldBeFalse();
        canvas.CurrentEditGroup.ShouldBeNull();
    }

    [Fact]
    public void ESC_ExitsNestedGroupEditMode_ReturnsToParent()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var parentGroup = new ComponentGroup("Parent");
        var childGroup = new ComponentGroup("Child");
        parentGroup.AddChild(childGroup);
        canvas.AddComponent(parentGroup);

        canvas.EnterGroupEditMode(parentGroup);
        canvas.EnterGroupEditMode(childGroup);
        canvas.CurrentEditGroup.ShouldBe(childGroup);

        // Act - Simulate ESC key behavior (exit one level)
        canvas.ExitGroupEditMode();

        // Assert - Should return to parent group edit mode
        canvas.IsInGroupEditMode.ShouldBeTrue();
        canvas.CurrentEditGroup.ShouldBe(parentGroup);
    }

    [Fact]
    public void ESC_ExitsLastGroupEditLevel_ReturnsToRootMode()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var group = new ComponentGroup("TestGroup");
        canvas.AddComponent(group);
        canvas.EnterGroupEditMode(group);

        // Act - Simulate ESC key behavior (exit from single-level edit mode)
        canvas.ExitGroupEditMode();

        // Assert - Should return to root (normal canvas mode)
        canvas.IsInGroupEditMode.ShouldBeFalse();
        canvas.CurrentEditGroup.ShouldBeNull();
        canvas.BreadcrumbPath.Count.ShouldBe(0);
    }

    [Fact]
    public void ExitGroupEditMode_WhenNotInEditMode_DoesNothing()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        canvas.IsInGroupEditMode.ShouldBeFalse();

        // Act - Simulate ESC when not in edit mode
        canvas.ExitGroupEditMode();

        // Assert - Should remain in normal mode (no exception)
        canvas.IsInGroupEditMode.ShouldBeFalse();
        canvas.CurrentEditGroup.ShouldBeNull();
    }
}
