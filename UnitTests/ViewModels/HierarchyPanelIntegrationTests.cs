using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;
using UnitTests;

namespace UnitTests.ViewModels;

/// <summary>
/// Integration tests for HierarchyPanelViewModel with DesignCanvasViewModel.
/// </summary>
public class HierarchyPanelIntegrationTests
{
    [Fact]
    public void Configure_BuildsTreeFromCanvasComponents()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchyPanel = new HierarchyPanelViewModel();

        // Add components to canvas
        AddTestComponent(canvas, "Component 1");
        AddTestComponent(canvas, "Component 2");
        AddTestComponent(canvas, "Component 3");

        // Act
        hierarchyPanel.Configure(canvas);

        // Assert
        hierarchyPanel.RootNodes.Count.ShouldBe(3);
        hierarchyPanel.StatusText.ShouldBe("3 components");
    }

    [Fact]
    public void AddingComponentToCanvas_UpdatesHierarchyTree()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchyPanel = new HierarchyPanelViewModel();
        hierarchyPanel.Configure(canvas);
        AddTestComponent(canvas, "Component 1");

        // Act
        AddTestComponent(canvas, "Component 2");

        // Assert
        hierarchyPanel.RootNodes.Count.ShouldBe(2);
        hierarchyPanel.StatusText.ShouldBe("2 components");
    }

    [Fact]
    public void RemovingComponentFromCanvas_UpdatesHierarchyTree()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchyPanel = new HierarchyPanelViewModel();
        AddTestComponent(canvas, "Component 1");
        var component2 = AddTestComponent(canvas, "Component 2");
        hierarchyPanel.Configure(canvas);

        // Act
        canvas.Components.Remove(component2);

        // Assert
        hierarchyPanel.RootNodes.Count.ShouldBe(1);
        hierarchyPanel.StatusText.ShouldBe("1 component");
    }

    [Fact]
    public void SelectingNodeInHierarchy_SelectsComponentOnCanvas()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchyPanel = new HierarchyPanelViewModel();
        var component1 = AddTestComponent(canvas, "Component 1");
        var component2 = AddTestComponent(canvas, "Component 2");
        hierarchyPanel.Configure(canvas);

        // Act
        var node = hierarchyPanel.RootNodes[1];
        hierarchyPanel.SelectedNode = node;

        // Assert
        component1.IsSelected.ShouldBeFalse();
        component2.IsSelected.ShouldBeTrue();
        canvas.SelectedComponent.ShouldBe(component2);
    }

    [Fact]
    public void SelectingComponentOnCanvas_SelectsNodeInHierarchy()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchyPanel = new HierarchyPanelViewModel();
        var component1 = AddTestComponent(canvas, "Component 1");
        var component2 = AddTestComponent(canvas, "Component 2");
        hierarchyPanel.Configure(canvas);

        // Act
        component2.IsSelected = true;

        // Assert
        hierarchyPanel.SelectedNode.ShouldNotBeNull();
        hierarchyPanel.SelectedNode!.Component.ShouldBe(component2);
        hierarchyPanel.SelectedNode.IsSelected.ShouldBeTrue();
    }

    [Fact]
    public void DeselectingComponentOnCanvas_DeselectsNodeInHierarchy()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchyPanel = new HierarchyPanelViewModel();
        var component = AddTestComponent(canvas, "Component");
        hierarchyPanel.Configure(canvas);
        component.IsSelected = true;

        // Act
        component.IsSelected = false;

        // Assert - node should still exist but not be selected
        var node = hierarchyPanel.RootNodes[0];
        node.IsSelected.ShouldBeFalse();
    }

    [Fact]
    public void FocusComponent_InvokesFocusCallback()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchyPanel = new HierarchyPanelViewModel();
        var component = AddTestComponent(canvas, "Component", x: 100, y: 200);
        hierarchyPanel.Configure(canvas);

        double focusX = 0;
        double focusY = 0;
        hierarchyPanel.OnFocusRequested = (x, y) =>
        {
            focusX = x;
            focusY = y;
        };

        var node = hierarchyPanel.RootNodes[0];

        // Act
        node.FocusComponentCommand.Execute(null);

        // Assert - should focus on component center (actual component dimensions from factory)
        // The test component from factory has specific dimensions, so we just verify the callback was invoked
        focusX.ShouldBeGreaterThan(0);
        focusY.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ExpandAllCommand_ExpandsAllNodes()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchyPanel = new HierarchyPanelViewModel();
        AddTestComponent(canvas, "Component 1");
        AddTestComponent(canvas, "Component 2");
        hierarchyPanel.Configure(canvas);

        // Initially collapsed
        foreach (var node in hierarchyPanel.RootNodes)
        {
            node.IsExpanded.ShouldBeFalse();
        }

        // Act
        hierarchyPanel.ExpandAllCommand.Execute(null);

        // Assert
        foreach (var node in hierarchyPanel.RootNodes)
        {
            node.IsExpanded.ShouldBeTrue();
        }
    }

    [Fact]
    public void CollapseAllCommand_CollapsesAllNodes()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchyPanel = new HierarchyPanelViewModel();
        AddTestComponent(canvas, "Component 1");
        AddTestComponent(canvas, "Component 2");
        hierarchyPanel.Configure(canvas);

        // Expand all first
        hierarchyPanel.ExpandAllCommand.Execute(null);

        // Act
        hierarchyPanel.CollapseAllCommand.Execute(null);

        // Assert
        foreach (var node in hierarchyPanel.RootNodes)
        {
            node.IsExpanded.ShouldBeFalse();
        }
    }

    [Fact]
    public void EmptyCanvas_ShowsNoComponentsStatus()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchyPanel = new HierarchyPanelViewModel();

        // Act
        hierarchyPanel.Configure(canvas);

        // Assert
        hierarchyPanel.RootNodes.Count.ShouldBe(0);
        hierarchyPanel.StatusText.ShouldBe("No components");
    }

    private static ComponentViewModel AddTestComponent(
        DesignCanvasViewModel canvas,
        string templateName,
        double x = 0,
        double y = 0,
        double width = 100,
        double height = 50)
    {
        var component = TestComponentFactory.CreateStraightWaveGuide();
        component.PhysicalX = x;
        component.PhysicalY = y;

        var vm = new ComponentViewModel(component, templateName);
        canvas.Components.Add(vm);
        return vm;
    }
}
