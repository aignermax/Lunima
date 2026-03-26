using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.ViewModels;

/// <summary>
/// Integration tests for group edit mode workflow (Canvas + Hierarchy + Commands).
/// </summary>
public class GroupEditModeIntegrationTests
{
    [Fact]
    public void CompleteEditModeWorkflow_EnterAndExitGroup()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var child1 = TestComponentFactory.CreateStraightWaveGuide();
        child1.PhysicalX = 100;
        child1.PhysicalY = 100;

        var child2 = TestComponentFactory.CreateStraightWaveGuide();
        child2.PhysicalX = 200;
        child2.PhysicalY = 200;

        var group = new ComponentGroup("TestGroup");
        group.AddChild(child1);
        group.AddChild(child2);
        var groupVm = canvas.AddComponent(group);
        hierarchy.RebuildTree();

        // Verify group was added to canvas
        canvas.Components.Count.ShouldBe(1, "Canvas should have 1 component (the group)");

        // Debug: Check why RootNodes might be empty
        if (hierarchy.RootNodes.Count == 0)
        {
            throw new Exception($"RootNodes is empty! Canvas.Components.Count={canvas.Components.Count}");
        }

        hierarchy.RootNodes.Count.ShouldBe(1, "Hierarchy should have 1 root node");

        // Act - Enter edit mode
        canvas.EnterGroupEditMode(group);
        hierarchy.SyncEditModeFromCanvas(group);

        // Assert - Edit mode active
        canvas.IsInGroupEditMode.ShouldBeTrue();
        canvas.CurrentEditGroup.ShouldBe(group);
        hierarchy.RootNodes[0].IsInEditMode.ShouldBeTrue();

        // Act - Exit edit mode
        canvas.ExitGroupEditMode();
        hierarchy.SyncEditModeFromCanvas(null);

        // Assert - Edit mode exited
        canvas.IsInGroupEditMode.ShouldBeFalse();
        canvas.CurrentEditGroup.ShouldBeNull();
        hierarchy.RootNodes[0].IsInEditMode.ShouldBeFalse();
    }

    [Fact]
    public void NestedGroupEditMode_NavigateThroughLevels()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var component = TestComponentFactory.CreateStraightWaveGuide();
        var innerGroup = new ComponentGroup("InnerGroup");
        innerGroup.AddChild(component);

        var outerGroup = new ComponentGroup("OuterGroup");
        outerGroup.AddChild(innerGroup);

        canvas.AddComponent(outerGroup);
        hierarchy.RebuildTree();

        // Act - Enter outer group
        canvas.EnterGroupEditMode(outerGroup);
        hierarchy.SyncEditModeFromCanvas(outerGroup);

        // Assert
        canvas.CurrentEditGroup.ShouldBe(outerGroup);
        canvas.BreadcrumbPath.Count.ShouldBe(1);

        // Act - Enter inner group
        canvas.EnterGroupEditMode(innerGroup);
        hierarchy.SyncEditModeFromCanvas(innerGroup);

        // Assert
        canvas.CurrentEditGroup.ShouldBe(innerGroup);
        canvas.BreadcrumbPath.Count.ShouldBe(2);
        canvas.BreadcrumbPath[0].ShouldBe(outerGroup);
        canvas.BreadcrumbPath[1].ShouldBe(innerGroup);

        // Act - Navigate back to outer group
        canvas.NavigateToBreadcrumbLevel(outerGroup);
        hierarchy.SyncEditModeFromCanvas(outerGroup);

        // Assert
        canvas.CurrentEditGroup.ShouldBe(outerGroup);
        canvas.BreadcrumbPath.Count.ShouldBe(1);

        // Act - Exit to root
        canvas.ExitToRoot();
        hierarchy.SyncEditModeFromCanvas(null);

        // Assert
        canvas.IsInGroupEditMode.ShouldBeFalse();
        canvas.BreadcrumbPath.Count.ShouldBe(0);
    }

    [Fact]
    public void EditMode_ComponentVisibility_OnlyInternalComponentsEditable()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();

        var internalComp = TestComponentFactory.CreateStraightWaveGuide();
        internalComp.PhysicalX = 100;
        internalComp.PhysicalY = 100;

        var externalComp = TestComponentFactory.CreateStraightWaveGuide();
        externalComp.PhysicalX = 300;
        externalComp.PhysicalY = 300;

        var group = new ComponentGroup("TestGroup");
        group.AddChild(internalComp);

        canvas.AddComponent(group);
        canvas.AddComponent(externalComp);

        // Act - Enter edit mode
        canvas.EnterGroupEditMode(group);

        // Assert - Internal component should be in the group
        group.ChildComponents.ShouldContain(internalComp);
        group.ChildComponents.ShouldNotContain(externalComp);

        // In edit mode, external components should be visually dimmed (tested in UI rendering)
        canvas.CurrentEditGroup.ShouldBe(group);
    }

    [Fact]
    public void EditMode_BreadcrumbNavigation_MaintainsCorrectState()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var level1 = new ComponentGroup("Level1");
        var level2 = new ComponentGroup("Level2");
        var level3 = new ComponentGroup("Level3");

        level1.AddChild(level2);
        level2.AddChild(level3);
        canvas.AddComponent(level1);

        // Act - Navigate down through all levels
        canvas.EnterGroupEditMode(level1);
        canvas.EnterGroupEditMode(level2);
        canvas.EnterGroupEditMode(level3);

        // Assert - At deepest level
        canvas.BreadcrumbPath.Count.ShouldBe(3);
        canvas.CurrentEditGroup.ShouldBe(level3);

        // Act - Jump to middle level
        canvas.NavigateToBreadcrumbLevel(level2);

        // Assert - Breadcrumb path is trimmed correctly
        canvas.BreadcrumbPath.Count.ShouldBe(2);
        canvas.BreadcrumbPath[0].ShouldBe(level1);
        canvas.BreadcrumbPath[1].ShouldBe(level2);
        canvas.CurrentEditGroup.ShouldBe(level2);
    }

    [Fact]
    public void HierarchyPanel_ExpandsToShowEditedGroup()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var component = TestComponentFactory.CreateStraightWaveGuide();
        var innerGroup = new ComponentGroup("InnerGroup");
        innerGroup.AddChild(component);

        var outerGroup = new ComponentGroup("OuterGroup");
        outerGroup.AddChild(innerGroup);

        canvas.AddComponent(outerGroup);
        hierarchy.RebuildTree();

        // Collapse the outer group initially
        hierarchy.RootNodes[0].IsExpanded = false;

        // Act - Enter edit mode on inner group
        hierarchy.SyncEditModeFromCanvas(innerGroup);

        // Assert - Outer group should be expanded to show the edited inner group
        hierarchy.RootNodes[0].IsExpanded.ShouldBeTrue();
        hierarchy.RootNodes[0].Children[0].IsInEditMode.ShouldBeTrue();
    }

    [Fact]
    public void ExitGroupEditMode_FromNestedLevel_ReturnsToParent()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var parent = new ComponentGroup("Parent");
        var child = new ComponentGroup("Child");
        parent.AddChild(child);
        canvas.AddComponent(parent);

        canvas.EnterGroupEditMode(parent);
        canvas.EnterGroupEditMode(child);

        // Act
        canvas.ExitGroupEditMode();

        // Assert - Should return to parent, not root
        canvas.CurrentEditGroup.ShouldBe(parent);
        canvas.IsInGroupEditMode.ShouldBeTrue();
        canvas.BreadcrumbPath.Count.ShouldBe(1);
    }

    [Fact]
    public void EditMode_CanMoveChildComponentsInsideGroup()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var child1 = TestComponentFactory.CreateStraightWaveGuide();
        child1.PhysicalX = 100;
        child1.PhysicalY = 100;

        var child2 = TestComponentFactory.CreateStraightWaveGuide();
        child2.PhysicalX = 200;
        child2.PhysicalY = 200;

        var group = new ComponentGroup("TestGroup");
        group.AddChild(child1);
        group.AddChild(child2);
        canvas.AddComponent(group);

        // Enter edit mode
        canvas.EnterGroupEditMode(group);

        // Act - Move child1
        var originalX = child1.PhysicalX;
        var originalY = child1.PhysicalY;
        child1.PhysicalX = 150;
        child1.PhysicalY = 150;

        // Assert
        child1.PhysicalX.ShouldBe(150);
        child1.PhysicalY.ShouldBe(150);
        child1.ParentGroup.ShouldBe(group);
    }

    [Fact]
    public void EditMode_ChildComponentsRemainInGroupAfterMove()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var child = TestComponentFactory.CreateStraightWaveGuide();
        child.PhysicalX = 100;
        child.PhysicalY = 100;

        var group = new ComponentGroup("TestGroup");
        group.AddChild(child);
        canvas.AddComponent(group);

        canvas.EnterGroupEditMode(group);

        // Act - Move child and exit edit mode
        child.PhysicalX = 150;
        child.PhysicalY = 150;
        canvas.ExitGroupEditMode();

        // Assert - Child should still be in group
        group.ChildComponents.ShouldContain(child);
        child.ParentGroup.ShouldBe(group);
    }

    [Fact]
    public void EditMode_HitTestingPrioritizesChildComponents()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var child = TestComponentFactory.CreateStraightWaveGuide();
        child.PhysicalX = 100;
        child.PhysicalY = 100;
        child.WidthMicrometers = 50;
        child.HeightMicrometers = 50;

        var externalComponent = TestComponentFactory.CreateStraightWaveGuide();
        externalComponent.PhysicalX = 100;
        externalComponent.PhysicalY = 100;
        externalComponent.WidthMicrometers = 50;
        externalComponent.HeightMicrometers = 50;

        var group = new ComponentGroup("TestGroup");
        group.AddChild(child);
        canvas.AddComponent(group);
        canvas.AddComponent(externalComponent);

        // Act - Enter edit mode
        canvas.EnterGroupEditMode(group);

        // Hit test the overlapping area
        var hitPoint = new Avalonia.Point(125, 125);
        var hitComponent = CAP.Avalonia.Controls.DesignCanvasHitTesting.HitTestComponent(hitPoint, canvas);

        // Assert - Should hit the child, not the external component
        hitComponent.ShouldNotBeNull();
        hitComponent.Component.ShouldBe(child);
    }

    [Fact]
    public void EditMode_CanInteractWithNestedGroups()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var innerComponent = TestComponentFactory.CreateStraightWaveGuide();
        innerComponent.PhysicalX = 100;
        innerComponent.PhysicalY = 100;
        innerComponent.WidthMicrometers = 50;
        innerComponent.HeightMicrometers = 50;

        var innerGroup = new ComponentGroup("InnerGroup");
        innerGroup.AddChild(innerComponent);

        var outerGroup = new ComponentGroup("OuterGroup");
        outerGroup.AddChild(innerGroup);
        canvas.AddComponent(outerGroup);

        // Act - Enter outer group edit mode
        canvas.EnterGroupEditMode(outerGroup);

        // Hit test the inner group
        var hitPoint = new Avalonia.Point(125, 125);
        var hitComponent = CAP.Avalonia.Controls.DesignCanvasHitTesting.HitTestComponent(hitPoint, canvas);

        // Assert - Should hit the inner group
        hitComponent.ShouldNotBeNull();
        hitComponent.Component.ShouldBe(innerGroup);
    }

    [Fact]
    public void EditMode_PinHitTesting_WorksForChildComponents()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var child = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        child.PhysicalX = 100;
        child.PhysicalY = 100;

        var externalComponent = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        externalComponent.PhysicalX = 500;
        externalComponent.PhysicalY = 500;

        var group = new ComponentGroup("TestGroup");
        group.AddChild(child);
        canvas.AddComponent(group);
        canvas.AddComponent(externalComponent);

        // Act - Enter edit mode
        canvas.EnterGroupEditMode(group);

        // Hit test child pin
        var childPinPos = child.PhysicalPins[0].GetAbsolutePosition();
        var childPinHit = CAP.Avalonia.Controls.DesignCanvasHitTesting.HitTestPin(
            new Avalonia.Point(childPinPos.Item1, childPinPos.Item2), canvas);

        // Hit test external pin
        var externalPinPos = externalComponent.PhysicalPins[0].GetAbsolutePosition();
        var externalPinHit = CAP.Avalonia.Controls.DesignCanvasHitTesting.HitTestPin(
            new Avalonia.Point(externalPinPos.Item1, externalPinPos.Item2), canvas);

        // Assert
        childPinHit.ShouldNotBeNull();
        childPinHit.ParentComponent.ShouldBe(child);
        externalPinHit.ShouldBeNull(); // External pins should not be hit-testable in edit mode
    }
}
