using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.ViewModels;

/// <summary>
/// Integration tests for HierarchyPanelViewModel with Canvas and Commands.
/// Tests the complete workflow: Create Group → Hierarchy Updates → Selection Sync.
/// </summary>
public class HierarchyPanelIntegrationTests
{
    [Fact]
    public void CreateGroupCommand_UpdatesHierarchyTree()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);
        var commandManager = new CommandManager();

        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        var comp2 = TestComponentFactory.CreateStraightWaveGuide();

        var vm1 = canvas.AddComponent(comp1, "Waveguide1");
        var vm2 = canvas.AddComponent(comp2, "Waveguide2");

        hierarchy.RebuildTree();
        hierarchy.RootNodes.Count.ShouldBe(2);

        canvas.Selection.SelectedComponents.Add(vm1);
        canvas.Selection.SelectedComponents.Add(vm2);

        // Act - Create a group
        var createGroupCmd = new CreateGroupCommand(canvas, new[] { vm1, vm2 }.ToList());
        commandManager.ExecuteCommand(createGroupCmd);

        // The hierarchy should automatically rebuild due to collection change
        // Assert
        hierarchy.RootNodes.Count.ShouldBe(1);
        hierarchy.RootNodes[0].IsGroup.ShouldBeTrue();
        hierarchy.RootNodes[0].Children.Count.ShouldBe(2);
    }

    [Fact]
    public void UngroupCommand_UpdatesHierarchyTree()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);
        var commandManager = new CommandManager();

        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        var comp2 = TestComponentFactory.CreateStraightWaveGuide();

        var group = new ComponentGroup("TestGroup");
        group.AddChild(comp1);
        group.AddChild(comp2);

        canvas.AddComponent(group, "Group");
        hierarchy.RebuildTree();

        // Act - Ungroup
        var ungroupCmd = new UngroupCommand(canvas, group);
        commandManager.ExecuteCommand(ungroupCmd);

        // Assert
        hierarchy.RootNodes.Count.ShouldBe(2);
        hierarchy.RootNodes.All(n => !n.IsGroup).ShouldBeTrue();
    }

    [Fact]
    public void SelectionSync_ComponentToHierarchy_HighlightsCorrectNode()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        var comp2 = TestComponentFactory.CreateStraightWaveGuide();

        var vm1 = canvas.AddComponent(comp1, "Waveguide1");
        var vm2 = canvas.AddComponent(comp2, "Waveguide2");

        hierarchy.RebuildTree();

        // Act - Simulate canvas selection
        hierarchy.SyncSelectionFromCanvas(vm2);

        // Assert
        hierarchy.RootNodes[0].IsSelected.ShouldBeFalse();
        hierarchy.RootNodes[1].IsSelected.ShouldBeTrue();
    }

    [Fact]
    public void SelectionSync_GroupWithChildren_WorksCorrectly()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var component1 = TestComponentFactory.CreateStraightWaveGuide();
        var component2 = TestComponentFactory.CreateStraightWaveGuide();

        var group = new ComponentGroup("TestGroup");
        group.AddChild(component1);
        group.AddChild(component2);

        var groupVm = canvas.AddComponent(group, "TestGroup");
        hierarchy.RebuildTree();

        // Collapse the group initially
        hierarchy.RootNodes[0].IsExpanded = false;

        // Act - Select the group (not a nested child, as those aren't in canvas.Components)
        hierarchy.SyncSelectionFromCanvas(groupVm);

        // Assert - Group should be selected
        hierarchy.RootNodes[0].IsSelected.ShouldBeTrue();
        // Note: Nested children are not directly selectable from canvas since they're not in Components collection
    }

    [Fact]
    public void FocusCommand_InvokesNavigationCallback()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var component = TestComponentFactory.CreateStraightWaveGuide();
        component.PhysicalX = 100;
        component.PhysicalY = 200;

        canvas.AddComponent(component, "Waveguide");
        hierarchy.RebuildTree();

        double? focusedX = null;
        double? focusedY = null;

        hierarchy.NavigateToPosition = (x, y) =>
        {
            focusedX = x;
            focusedY = y;
        };

        // Act
        hierarchy.RootNodes[0].FocusCommand.Execute(null);

        // Assert
        focusedX.ShouldNotBeNull();
        focusedY.ShouldNotBeNull();
    }

    [Fact]
    public void SelectCommand_FromHierarchyNode_UpdatesCanvasSelection()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var component = TestComponentFactory.CreateStraightWaveGuide();
        var vm = canvas.AddComponent(component, "Waveguide");

        hierarchy.RebuildTree();

        // Act - Click node in hierarchy
        hierarchy.RootNodes[0].SelectCommand.Execute(null);

        // Assert - Canvas selection should update
        vm.IsSelected.ShouldBeTrue();
        canvas.Selection.SelectedComponents.ShouldContain(vm);
    }

    [Fact]
    public void DeleteComponentCommand_UpdatesHierarchyTree()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);
        var commandManager = new CommandManager();

        var component = TestComponentFactory.CreateStraightWaveGuide();
        var vm = canvas.AddComponent(component, "Waveguide");

        hierarchy.RebuildTree();
        hierarchy.RootNodes.Count.ShouldBe(1);

        // Act - Delete component
        var deleteCmd = new DeleteComponentCommand(canvas, vm);
        commandManager.ExecuteCommand(deleteCmd);

        // Assert - Hierarchy should automatically update
        hierarchy.RootNodes.Count.ShouldBe(0);
    }

    [Fact]
    public void Undo_CreateGroup_RestoresHierarchyState()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);
        var commandManager = new CommandManager();

        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        var comp2 = TestComponentFactory.CreateStraightWaveGuide();

        var vm1 = canvas.AddComponent(comp1, "Waveguide1");
        var vm2 = canvas.AddComponent(comp2, "Waveguide2");

        hierarchy.RebuildTree();
        int originalRootCount = hierarchy.RootNodes.Count;

        canvas.Selection.SelectedComponents.Add(vm1);
        canvas.Selection.SelectedComponents.Add(vm2);

        var createGroupCmd = new CreateGroupCommand(canvas, new[] { vm1, vm2 }.ToList());
        commandManager.ExecuteCommand(createGroupCmd);

        // Act - Undo
        commandManager.Undo();

        // Assert - Hierarchy should restore to original state
        hierarchy.RootNodes.Count.ShouldBe(originalRootCount);
    }
}
