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
}
