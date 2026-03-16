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

    [Fact]
    public void FocusCommand_SelectsComponentAndNavigates_CompleteFlow()
    {
        // Arrange - Setup complete canvas + hierarchy integration
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        comp1.PhysicalX = 100;
        comp1.PhysicalY = 150;
        comp1.WidthMicrometers = 60;
        comp1.HeightMicrometers = 40;

        var comp2 = TestComponentFactory.CreateStraightWaveGuide();
        comp2.PhysicalX = 300;
        comp2.PhysicalY = 250;
        comp2.WidthMicrometers = 80;
        comp2.HeightMicrometers = 60;

        var vm1 = canvas.AddComponent(comp1, "Waveguide1");
        var vm2 = canvas.AddComponent(comp2, "Waveguide2");

        hierarchy.RebuildTree();

        // Pre-select first component
        vm1.IsSelected = true;
        canvas.Selection.SelectedComponents.Add(vm1);
        canvas.SelectedComponent = vm1;

        bool navigationCalled = false;
        double? navigatedX = null;
        double? navigatedY = null;

        hierarchy.NavigateToPosition = (x, y) =>
        {
            navigationCalled = true;
            navigatedX = x;
            navigatedY = y;
        };

        // Act - Focus on second component via hierarchy target icon
        var node2 = hierarchy.RootNodes[1];
        node2.FocusCommand.Execute(null);

        // Assert - Complete flow:
        // 1. Previous selection cleared
        vm1.IsSelected.ShouldBeFalse();
        canvas.Selection.SelectedComponents.ShouldNotContain(vm1);

        // 2. New component selected
        vm2.IsSelected.ShouldBeTrue();
        canvas.SelectedComponent.ShouldBe(vm2);
        canvas.Selection.SelectedComponents.ShouldContain(vm2);
        canvas.Selection.SelectedComponents.Count.ShouldBe(1);

        // 3. Canvas navigated to component center
        navigationCalled.ShouldBeTrue();
        navigatedX.ShouldBe(340); // 300 + 80/2
        navigatedY.ShouldBe(280); // 250 + 60/2

        // 4. Hierarchy selection synced
        hierarchy.RootNodes[0].IsSelected.ShouldBeFalse();
        hierarchy.RootNodes[1].IsSelected.ShouldBeTrue();
    }

    [Fact]
    public void FocusCommand_WithMultipleComponents_SelectsOnlyTargetComponent()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        var comp2 = TestComponentFactory.CreateStraightWaveGuide();
        var comp3 = TestComponentFactory.CreateStraightWaveGuide();

        var vm1 = canvas.AddComponent(comp1, "Waveguide1");
        var vm2 = canvas.AddComponent(comp2, "Waveguide2");
        var vm3 = canvas.AddComponent(comp3, "Waveguide3");

        hierarchy.RebuildTree();

        // Pre-select multiple components
        vm1.IsSelected = true;
        vm2.IsSelected = true;
        canvas.Selection.SelectedComponents.Add(vm1);
        canvas.Selection.SelectedComponents.Add(vm2);

        // Act - Focus on third component
        hierarchy.RootNodes[2].FocusCommand.Execute(null);

        // Assert - Only third component should be selected
        vm1.IsSelected.ShouldBeFalse();
        vm2.IsSelected.ShouldBeFalse();
        vm3.IsSelected.ShouldBeTrue();
        canvas.Selection.SelectedComponents.Count.ShouldBe(1);
        canvas.Selection.SelectedComponents[0].ShouldBe(vm3);
    }

    [Fact]
    public void FocusCommand_OnGroupNode_SelectsAndNavigatesToGroup()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var child1 = TestComponentFactory.CreateStraightWaveGuide();
        var child2 = TestComponentFactory.CreateStraightWaveGuide();

        var group = new ComponentGroup("TestGroup");
        group.PhysicalX = 200;
        group.PhysicalY = 300;
        group.AddChild(child1);
        group.AddChild(child2);

        var groupVm = canvas.AddComponent(group, "Group");
        hierarchy.RebuildTree();

        bool navigationCalled = false;
        double? navigatedX = null;
        double? navigatedY = null;

        hierarchy.NavigateToPosition = (x, y) =>
        {
            navigationCalled = true;
            navigatedX = x;
            navigatedY = y;
        };

        // Act - Focus on group via hierarchy
        var groupNode = hierarchy.RootNodes[0];
        groupNode.FocusCommand.Execute(null);

        // Assert
        groupVm.IsSelected.ShouldBeTrue();
        canvas.SelectedComponent.ShouldBe(groupVm);
        navigationCalled.ShouldBeTrue();
        navigatedX.ShouldNotBeNull();
        navigatedY.ShouldNotBeNull();
    }

    [Fact]
    public void RenameGroup_UpdatesHierarchyDisplayName_Immediately()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var child1 = TestComponentFactory.CreateStraightWaveGuide();
        var child2 = TestComponentFactory.CreateStraightWaveGuide();

        var group = new ComponentGroup("Original Name");
        group.AddChild(child1);
        group.AddChild(child2);

        canvas.AddComponent(group, "Original Name");
        hierarchy.RebuildTree();

        var groupNode = hierarchy.RootNodes[0];
        string initialDisplayName = groupNode.DisplayName;

        // Act - Rename the group directly (simulating command execution)
        group.GroupName = "Renamed Group";

        // Assert - DisplayName should update immediately without rebuilding tree
        groupNode.DisplayName.ShouldBe("Renamed Group (2)");
        groupNode.DisplayName.ShouldNotBe(initialDisplayName);
    }

    [Fact]
    public void RenameGroup_MultipleTimesInSession_UpdatesHierarchyEachTime()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var child = TestComponentFactory.CreateStraightWaveGuide();
        var group = new ComponentGroup("Name1");
        group.AddChild(child);

        canvas.AddComponent(group, "Name1");
        hierarchy.RebuildTree();

        var groupNode = hierarchy.RootNodes[0];

        // Act & Assert - Rename multiple times
        group.GroupName = "Name2";
        groupNode.DisplayName.ShouldBe("Name2 (1)");

        group.GroupName = "Name3";
        groupNode.DisplayName.ShouldBe("Name3 (1)");

        group.GroupName = "Final Name";
        groupNode.DisplayName.ShouldBe("Final Name (1)");
    }

    [Fact]
    public void RenameGroup_WithPropertyChangedEvent_TriggersHierarchyUpdate()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var group = new ComponentGroup("Original");
        var child = TestComponentFactory.CreateStraightWaveGuide();
        group.AddChild(child);

        canvas.AddComponent(group, "Original");
        hierarchy.RebuildTree();

        var groupNode = hierarchy.RootNodes[0];

        // Monitor PropertyChanged events on the node
        int displayNameChangedCount = 0;
        groupNode.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(groupNode.DisplayName))
            {
                displayNameChangedCount++;
            }
        };

        // Act - Rename the group
        group.GroupName = "New Name";

        // Assert - PropertyChanged should have been raised on the node
        displayNameChangedCount.ShouldBeGreaterThan(0);
        groupNode.DisplayName.ShouldContain("New Name");
    }
}
