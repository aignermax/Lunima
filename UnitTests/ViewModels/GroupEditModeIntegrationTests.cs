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
        canvas.AddComponent(group);
        hierarchy.RebuildTree();

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
}
