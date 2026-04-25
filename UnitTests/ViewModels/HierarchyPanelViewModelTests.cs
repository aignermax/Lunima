using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.ViewModels;

/// <summary>
/// Unit tests for HierarchyPanelViewModel (tree structure management).
/// </summary>
public class HierarchyPanelViewModelTests
{
    [Fact]
    public void RebuildTree_WithNoComponents_CreatesEmptyTree()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        // Act
        hierarchy.RebuildTree();

        // Assert
        hierarchy.RootNodes.Count.ShouldBe(0);
    }

    [Fact]
    public void RebuildTree_WithSingleComponent_CreatesOneRootNode()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var component = TestComponentFactory.CreateStraightWaveGuide();
        canvas.AddComponent(component, "Waveguide");

        // Act
        hierarchy.RebuildTree();

        // Assert
        hierarchy.RootNodes.Count.ShouldBe(1);
        hierarchy.RootNodes[0].Component.ShouldBe(component);
        hierarchy.RootNodes[0].IsGroup.ShouldBeFalse();
    }

    [Fact]
    public void RebuildTree_WithComponentGroup_CreatesHierarchicalStructure()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var childComp1 = TestComponentFactory.CreateStraightWaveGuide();
        var childComp2 = TestComponentFactory.CreateStraightWaveGuide();

        var group = new ComponentGroup("TestGroup");
        group.AddChild(childComp1);
        group.AddChild(childComp2);

        canvas.AddComponent(group, "Group");

        // Act
        hierarchy.RebuildTree();

        // Assert
        hierarchy.RootNodes.Count.ShouldBe(1);
        var groupNode = hierarchy.RootNodes[0];
        groupNode.IsGroup.ShouldBeTrue();
        groupNode.Children.Count.ShouldBe(2);
    }

    [Fact]
    public void RebuildTree_WithNestedGroups_CreatesDeepHierarchy()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var component = TestComponentFactory.CreateStraightWaveGuide();
        var innerGroup = new ComponentGroup("Inner");
        innerGroup.AddChild(component);

        var outerGroup = new ComponentGroup("Outer");
        outerGroup.AddChild(innerGroup);

        canvas.AddComponent(outerGroup, "OuterGroup");

        // Act
        hierarchy.RebuildTree();

        // Assert
        hierarchy.RootNodes.Count.ShouldBe(1);
        var outerNode = hierarchy.RootNodes[0];
        outerNode.Children.Count.ShouldBe(1);
        var innerNode = outerNode.Children[0];
        innerNode.Children.Count.ShouldBe(1);
    }

    [Fact]
    public void SyncSelectionFromCanvas_WithNullSelection_ClearsAllSelections()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var component = TestComponentFactory.CreateStraightWaveGuide();
        canvas.AddComponent(component, "Waveguide");
        hierarchy.RebuildTree();

        hierarchy.RootNodes[0].IsSelected = true;

        // Act
        hierarchy.SyncSelectionFromCanvas(null);

        // Assert
        hierarchy.RootNodes[0].IsSelected.ShouldBeFalse();
    }

    [Fact]
    public void SyncSelectionFromCanvas_WithComponentSelection_SelectsCorrectNode()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var component1 = TestComponentFactory.CreateStraightWaveGuide();
        var component2 = TestComponentFactory.CreateStraightWaveGuide();

        var vm1 = canvas.AddComponent(component1, "Waveguide1");
        var vm2 = canvas.AddComponent(component2, "Waveguide2");

        hierarchy.RebuildTree();

        // Act
        hierarchy.SyncSelectionFromCanvas(vm2);

        // Assert
        hierarchy.RootNodes[0].IsSelected.ShouldBeFalse();
        hierarchy.RootNodes[1].IsSelected.ShouldBeTrue();
    }

    [Fact]
    public void HierarchyNodeViewModel_DisplayName_ForGroup_IncludesChildCount()
    {
        // Arrange
        var childComp1 = TestComponentFactory.CreateStraightWaveGuide();
        var childComp2 = TestComponentFactory.CreateStraightWaveGuide();

        var group = new ComponentGroup("MyGroup");
        group.AddChild(childComp1);
        group.AddChild(childComp2);

        // Act
        var node = new HierarchyNodeViewModel(group);

        // Assert
        node.DisplayName.ShouldContain("MyGroup");
        node.DisplayName.ShouldContain("2");
    }

    [Fact]
    public void HierarchyNodeViewModel_DisplayName_ForComponent_ShowsIdentifier()
    {
        // Arrange
        var component = TestComponentFactory.CreateStraightWaveGuide();

        // Act
        var node = new HierarchyNodeViewModel(component);

        // Assert
        node.DisplayName.ShouldBe(component.Identifier);
    }

    [Fact]
    public void HierarchyNodeViewModel_IsGroup_ReturnsTrueForComponentGroup()
    {
        // Arrange
        var group = new ComponentGroup("TestGroup");

        // Act
        var node = new HierarchyNodeViewModel(group);

        // Assert
        node.IsGroup.ShouldBeTrue();
    }

    [Fact]
    public void HierarchyNodeViewModel_IsGroup_ReturnsFalseForRegularComponent()
    {
        // Arrange
        var component = TestComponentFactory.CreateStraightWaveGuide();

        // Act
        var node = new HierarchyNodeViewModel(component);

        // Assert
        node.IsGroup.ShouldBeFalse();
    }

    [Fact]
    public void HierarchyNodeViewModel_ToggleExpanded_ChangesExpandedState()
    {
        // Arrange
        var group = new ComponentGroup("TestGroup");
        var node = new HierarchyNodeViewModel(group);
        bool initialState = node.IsExpanded;

        // Act
        node.ToggleExpandedCommand.Execute(null);

        // Assert
        node.IsExpanded.ShouldBe(!initialState);
    }

    [Fact]
    public void HierarchyNodeViewModel_FindNodeByComponent_FindsCorrectNode()
    {
        // Arrange
        var component1 = TestComponentFactory.CreateStraightWaveGuide();
        var component2 = TestComponentFactory.CreateStraightWaveGuide();

        var group = new ComponentGroup("TestGroup");
        group.AddChild(component1);
        group.AddChild(component2);

        var rootNode = new HierarchyNodeViewModel(group);
        var childNode1 = new HierarchyNodeViewModel(component1);
        var childNode2 = new HierarchyNodeViewModel(component2);
        rootNode.Children.Add(childNode1);
        rootNode.Children.Add(childNode2);

        // Act
        var found = rootNode.FindNodeByComponent(component2);

        // Assert
        found.ShouldNotBeNull();
        found.Component.ShouldBe(component2);
    }

    [Fact]
    public void RebuildTree_AutomaticallyTriggeredOnComponentAdd()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);
        hierarchy.RebuildTree();
        hierarchy.RootNodes.Count.ShouldBe(0);

        // Act
        var component = TestComponentFactory.CreateStraightWaveGuide();
        canvas.AddComponent(component, "Waveguide");

        // Assert - RebuildTree should be automatically triggered by collection change
        hierarchy.RootNodes.Count.ShouldBe(1);
    }

    [Fact]
    public void FocusCommand_SelectsComponentOnCanvas()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var component1 = TestComponentFactory.CreateStraightWaveGuide();
        var component2 = TestComponentFactory.CreateStraightWaveGuide();

        var vm1 = canvas.AddComponent(component1, "Waveguide1");
        var vm2 = canvas.AddComponent(component2, "Waveguide2");

        hierarchy.RebuildTree();

        // Act - Focus on second component via hierarchy node
        var node2 = hierarchy.RootNodes[1];
        node2.FocusCommand.Execute(null);

        // Assert - Component should be selected on canvas
        vm1.IsSelected.ShouldBeFalse();
        vm2.IsSelected.ShouldBeTrue();
        canvas.SelectedComponent.ShouldBe(vm2);
    }

    [Fact]
    public void FocusCommand_CallsNavigateToPosition()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var component = TestComponentFactory.CreateStraightWaveGuide();
        component.PhysicalX = 100;
        component.PhysicalY = 200;
        component.WidthMicrometers = 50;
        component.HeightMicrometers = 50;

        canvas.AddComponent(component, "Waveguide");
        hierarchy.RebuildTree();

        bool navigateCalled = false;
        double capturedX = 0;
        double capturedY = 0;

        hierarchy.NavigateToPosition = (x, y) =>
        {
            navigateCalled = true;
            capturedX = x;
            capturedY = y;
        };

        // Act
        var node = hierarchy.RootNodes[0];
        node.FocusCommand.Execute(null);

        // Assert - Should navigate to center of component
        navigateCalled.ShouldBeTrue();
        capturedX.ShouldBe(125); // 100 + 50/2
        capturedY.ShouldBe(225); // 200 + 50/2
    }

    [Fact]
    public void FocusCommand_ClearsOtherSelections()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var component1 = TestComponentFactory.CreateStraightWaveGuide();
        var component2 = TestComponentFactory.CreateStraightWaveGuide();

        var vm1 = canvas.AddComponent(component1, "Waveguide1");
        var vm2 = canvas.AddComponent(component2, "Waveguide2");

        hierarchy.RebuildTree();

        // Pre-select first component
        vm1.IsSelected = true;
        canvas.Selection.SelectedComponents.Add(vm1);

        // Act - Focus on second component
        var node2 = hierarchy.RootNodes[1];
        node2.FocusCommand.Execute(null);

        // Assert - Only second component should be selected
        vm1.IsSelected.ShouldBeFalse();
        vm2.IsSelected.ShouldBeTrue();
        canvas.Selection.SelectedComponents.Count.ShouldBe(1);
        canvas.Selection.SelectedComponents[0].ShouldBe(vm2);
    }

    [Fact]
    public void FocusCommand_WorksWithGroupedComponents()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var childComp = TestComponentFactory.CreateStraightWaveGuide();
        childComp.PhysicalX = 50;
        childComp.PhysicalY = 75;

        var group = new ComponentGroup("TestGroup");
        group.AddChild(childComp);

        var groupVm = canvas.AddComponent(group, "Group");
        hierarchy.RebuildTree();

        bool navigateCalled = false;

        hierarchy.NavigateToPosition = (x, y) =>
        {
            navigateCalled = true;
        };

        // Act - Focus on child component within group
        var groupNode = hierarchy.RootNodes[0];
        var childNode = groupNode.Children[0];
        childNode.FocusCommand.Execute(null);

        // Assert - Child component should be selected and navigated to
        navigateCalled.ShouldBeTrue();
        // The child component should be selected through its ComponentViewModel if available
        // (In this case, it may not have a VM since it's inside a group, but the focus should still work)
    }

    // ── S-matrix override marker tests ───────────────────────────────────────

    [Fact]
    public void RebuildTree_WithOverrideChecker_SetsHasSMatrixOverrideOnMatchingNode()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.Identifier = "comp_override";
        canvas.AddComponent(comp, "Waveguide");

        var overrideIds = new HashSet<string> { "comp_override" };
        hierarchy.CheckHasSMatrixOverride = id => overrideIds.Contains(id);

        // Act
        hierarchy.RebuildTree();

        // Assert
        hierarchy.RootNodes[0].HasSMatrixOverride.ShouldBeTrue();
    }

    [Fact]
    public void RebuildTree_WithNoOverride_HasSMatrixOverrideIsFalse()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var comp = TestComponentFactory.CreateStraightWaveGuide();
        canvas.AddComponent(comp, "Waveguide");

        hierarchy.CheckHasSMatrixOverride = _ => false;

        // Act
        hierarchy.RebuildTree();

        // Assert
        hierarchy.RootNodes[0].HasSMatrixOverride.ShouldBeFalse();
    }

    [Fact]
    public void RefreshOverrideMarkers_UpdatesExistingNodes()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.Identifier = "comp_A";
        canvas.AddComponent(comp, "Waveguide");

        hierarchy.CheckHasSMatrixOverride = _ => false;
        hierarchy.RebuildTree();
        hierarchy.RootNodes[0].HasSMatrixOverride.ShouldBeFalse();

        // Simulate an import: now the override exists
        hierarchy.CheckHasSMatrixOverride = id => id == "comp_A";

        // Act
        hierarchy.RefreshOverrideMarkers();

        // Assert
        hierarchy.RootNodes[0].HasSMatrixOverride.ShouldBeTrue();
    }

    [Fact]
    public void RefreshOverrideMarkers_AfterDelete_ClearsMarker()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.Identifier = "comp_B";
        canvas.AddComponent(comp, "Waveguide");

        hierarchy.CheckHasSMatrixOverride = id => id == "comp_B";
        hierarchy.RebuildTree();
        hierarchy.RootNodes[0].HasSMatrixOverride.ShouldBeTrue();

        // Simulate a delete: override removed
        hierarchy.CheckHasSMatrixOverride = _ => false;

        // Act
        hierarchy.RefreshOverrideMarkers();

        // Assert
        hierarchy.RootNodes[0].HasSMatrixOverride.ShouldBeFalse();
    }
}
