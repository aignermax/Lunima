using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Tests for issue #273: Bug: ComponentViewModel duplicates after multiple undo/redo cycles on CreateGroupCommand
/// </summary>
public class CreateGroupMultipleUndoRedoTests
{
    [Fact]
    public void CreateGroup_MultipleUndoRedo_NoViewModelDuplicates()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        // Create 2 components
        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        var vm1 = canvas.AddComponent(comp1);

        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.PhysicalX = 100;
        comp2.PhysicalY = 0;
        var vm2 = canvas.AddComponent(comp2);

        // Verify: 2 components
        canvas.Components.Count.ShouldBe(2, "Should have exactly 2 components after adding them");

        // Act: Create group
        var createGroupCmd = new CreateGroupCommand(
            canvas,
            new List<ComponentViewModel> { vm1, vm2 });
        commandManager.ExecuteCommand(createGroupCmd);

        // Verify: Should have 1 group
        canvas.Components.Count.ShouldBe(1, "Should have 1 group after creating group");
        canvas.Components[0].Component.ShouldBeOfType<ComponentGroup>();

        // Act: 3x Undo
        commandManager.Undo();
        commandManager.Undo();
        commandManager.Undo();

        // At this point, we've undone the group creation 3 times,
        // but only 1 undo was in the stack, so we should still have 2 components
        canvas.Components.Count.ShouldBe(2, "After 3x undo: should have 2 components");

        // Act: 3x Redo
        commandManager.Redo();
        commandManager.Redo();
        commandManager.Redo();

        // After 3x redo (but only 1 was available), should have 1 group
        canvas.Components.Count.ShouldBe(1, "After 3x redo: should have 1 group");

        // Act: 1x Undo
        commandManager.Undo();

        // Assert: CRITICAL - Should have EXACTLY 2 components, NO DUPLICATES
        canvas.Components.Count.ShouldBe(2,
            "After final undo, should have exactly 2 components (not 4 due to duplication!)");

        // Verify: Each Core Component has exactly 1 ViewModel
        var comp1VMs = canvas.Components.Where(vm => vm.Component == comp1).ToList();
        comp1VMs.Count.ShouldBe(1, "Component 1 should have exactly 1 ViewModel, not duplicates");

        var comp2VMs = canvas.Components.Where(vm => vm.Component == comp2).ToList();
        comp2VMs.Count.ShouldBe(1, "Component 2 should have exactly 1 ViewModel, not duplicates");

        // Verify: All ViewModels are unique instances
        var vmSet = new HashSet<ComponentViewModel>(canvas.Components);
        vmSet.Count.ShouldBe(canvas.Components.Count, "All ViewModels should be unique instances");

        // Verify: No groups remain
        canvas.Components.Count(c => c.Component is ComponentGroup).ShouldBe(0,
            "Should have 0 groups after undo");
    }

    [Fact]
    public void CreateGroup_MultipleUndoRedoCycles_MaintainsCorrectCount()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        canvas.AddComponent(comp1);
        canvas.AddComponent(comp2);

        var vms = canvas.Components.ToList();

        // Act: Create group
        var createGroupCmd = new CreateGroupCommand(canvas, vms);
        commandManager.ExecuteCommand(createGroupCmd);

        // Verify initial state
        canvas.Components.Count.ShouldBe(1, "Should have 1 group initially");

        // Act: 5 cycles of undo/redo
        for (int i = 0; i < 5; i++)
        {
            commandManager.Undo();
            canvas.Components.Count.ShouldBe(2, $"After undo cycle {i + 1}: should have 2 components");

            // Verify no duplicates after each undo
            var comp1Count = canvas.Components.Count(vm => vm.Component == comp1);
            var comp2Count = canvas.Components.Count(vm => vm.Component == comp2);
            comp1Count.ShouldBe(1, $"After undo cycle {i + 1}: comp1 should appear once");
            comp2Count.ShouldBe(1, $"After undo cycle {i + 1}: comp2 should appear once");

            commandManager.Redo();
            canvas.Components.Count.ShouldBe(1, $"After redo cycle {i + 1}: should have 1 group");
        }

        // Final undo
        commandManager.Undo();

        // Assert: No duplicates after many cycles
        canvas.Components.Count.ShouldBe(2, "After many cycles: should have exactly 2 components");
        canvas.Components.Count(vm => vm.Component == comp1).ShouldBe(1, "comp1 should appear exactly once");
        canvas.Components.Count(vm => vm.Component == comp2).ShouldBe(1, "comp2 should appear exactly once");
    }

    [Fact]
    public void CreateGroup_10UndoRedoCycles_NoMemoryLeak()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        var createGroupCmd = new CreateGroupCommand(canvas, new List<ComponentViewModel> { vm1, vm2 });
        commandManager.ExecuteCommand(createGroupCmd);

        // Track the ViewModels that were originally removed
        var originalVm1 = vm1;
        var originalVm2 = vm2;

        // Act: 10 cycles of undo/redo
        for (int i = 0; i < 10; i++)
        {
            commandManager.Undo();
            commandManager.Redo();
        }

        // Final undo to restore individual components
        commandManager.Undo();

        // Assert: The SAME ViewModel instances should be restored, not new ones
        canvas.Components.Count.ShouldBe(2);

        // After multiple cycles, the command should still restore the exact same ViewModels
        // (This verifies there's no creation of new ViewModels on each undo)
        var restoredVm1 = canvas.Components.FirstOrDefault(vm => vm.Component == comp1);
        var restoredVm2 = canvas.Components.FirstOrDefault(vm => vm.Component == comp2);

        restoredVm1.ShouldNotBeNull("Component 1 ViewModel should be restored");
        restoredVm2.ShouldNotBeNull("Component 2 ViewModel should be restored");

        // Verify they are the SAME instances (reference equality)
        ReferenceEquals(restoredVm1, originalVm1).ShouldBeTrue(
            "Restored ViewModel for comp1 should be the SAME instance as original");
        ReferenceEquals(restoredVm2, originalVm2).ShouldBeTrue(
            "Restored ViewModel for comp2 should be the SAME instance as original");
    }

    [Fact]
    public void CreateGroup_WithPlaceCommands_NoHierarchyDuplicates()
    {
        // This test reproduces the bug where Undo/Redo of CreateGroupCommand
        // causes duplicate entries in the Hierarchy panel and Canvas

        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        // Create 2 components manually (simulating PlaceComponentCommand)
        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        var vm1 = canvas.AddComponent(comp1);

        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.PhysicalX = 100;
        comp2.PhysicalY = 0;
        var vm2 = canvas.AddComponent(comp2);

        // Verify: 2 components in canvas and hierarchy
        canvas.Components.Count.ShouldBe(2, "Should have 2 components after placing them");
        CountAllHierarchyNodes(hierarchy).ShouldBe(2, "Hierarchy should show 2 components");

        // Act: Create group from the 2 components
        var createGroupCmd = new CreateGroupCommand(canvas, new List<ComponentViewModel> { vm1, vm2 });
        commandManager.ExecuteCommand(createGroupCmd);

        // Verify: 1 group in canvas (with 2 children inside)
        canvas.Components.Count.ShouldBe(1, "Should have 1 group after creating group");
        canvas.Components[0].Component.ShouldBeOfType<ComponentGroup>();

        var group = (ComponentGroup)canvas.Components[0].Component;
        group.ChildComponents.Count.ShouldBe(2, "Group should contain 2 child components");

        // CRITICAL: Hierarchy should show 1 group + 2 children = 3 total nodes
        CountAllHierarchyNodes(hierarchy).ShouldBe(3,
            "Hierarchy should show 1 group with 2 children (3 nodes total)");

        // Act: Undo the group creation
        commandManager.Undo();

        // Verify: Back to 2 components
        canvas.Components.Count.ShouldBe(2, "After undo: should have 2 components");
        CountAllHierarchyNodes(hierarchy).ShouldBe(2, "After undo: hierarchy should show 2 components");

        // Act: Redo the group creation
        commandManager.Redo();

        // Verify: Back to 1 group
        canvas.Components.Count.ShouldBe(1, "After redo: should have 1 group");

        // CRITICAL BUG CHECK: Hierarchy should STILL show only 3 nodes (1 group + 2 children)
        // If this fails, it means duplicate ViewModels were created
        var hierarchyCount = CountAllHierarchyNodes(hierarchy);
        hierarchyCount.ShouldBe(3,
            $"After redo: hierarchy should show 1 group with 2 children (3 nodes total), but found {hierarchyCount}");

        // Additional verification: Canvas should have exactly 1 ComponentViewModel
        canvas.Components.Count.ShouldBe(1, "Canvas should have exactly 1 component (the group)");

        // Verify: No duplicate ViewModels for the same Core Component
        var groupVm = canvas.Components[0];
        var duplicateGroupVms = canvas.Components.Where(vm => vm.Component == groupVm.Component).ToList();
        duplicateGroupVms.Count.ShouldBe(1, "Should have exactly 1 ViewModel for the group");
    }

    [Fact]
    public void CreateGroup_HistoryNavigationToZeroAndForward_NoComponentDuplicates()
    {
        // This test reproduces the REAL bug: When you undo back to 0 (empty canvas),
        // then redo forward, CreateGroupCommand can't find the re-created components
        // because it stores old references!

        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        // Create 2 components manually
        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        var vm1 = canvas.AddComponent(comp1);

        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.PhysicalX = 100;
        comp2.PhysicalY = 0;
        var vm2 = canvas.AddComponent(comp2);

        canvas.Components.Count.ShouldBe(2, "Should have 2 components");

        // Create group
        var createGroupCmd = new CreateGroupCommand(canvas, new List<ComponentViewModel> { vm1, vm2 });
        commandManager.ExecuteCommand(createGroupCmd);

        canvas.Components.Count.ShouldBe(1, "Should have 1 group after creating group");

        // CRITICAL: Undo ALL THE WAY back to 0 (empty canvas)
        commandManager.Undo(); // Undo Create Group
        canvas.Components.Count.ShouldBe(2, "After undo group: 2 components");

        // Simulate what happens when history goes back further:
        // Components get removed from canvas
        canvas.RemoveComponent(canvas.Components[0]);
        canvas.RemoveComponent(canvas.Components[0]);
        canvas.Components.Count.ShouldBe(0, "Canvas should be empty");

        // Now re-add the SAME Core Component instances (simulating history going forward)
        // This creates NEW ViewModels!
        var vm1New = canvas.AddComponent(comp1); // NEW ViewModel for comp1!
        var vm2New = canvas.AddComponent(comp2); // NEW ViewModel for comp2!

        canvas.Components.Count.ShouldBe(2, "Should have 2 components after re-adding");

        // Verify these are DIFFERENT ViewModel instances
        ReferenceEquals(vm1, vm1New).ShouldBeFalse("vm1New should be a different instance than vm1");
        ReferenceEquals(vm2, vm2New).ShouldBeFalse("vm2New should be a different instance than vm2");

        // But they wrap the SAME Core Components
        ReferenceEquals(vm1.Component, vm1New.Component).ShouldBeTrue("Both VMs should wrap the same comp1");
        ReferenceEquals(vm2.Component, vm2New.Component).ShouldBeTrue("Both VMs should wrap the same comp2");

        // THE BUG: CreateGroupCommand Redo path uses _componentViewModels (contains vm1, vm2)
        // But canvas now has vm1New, vm2New!
        // When Redo tries to remove components (line 70: _canvas.Components.Remove(compVm)),
        // it removes vm1 and vm2, but those are NOT in the canvas anymore!
        // So vm1New and vm2New stay in canvas!

        commandManager.Redo(); // Redo Create Group

        // CRITICAL BUG CHECK: Should be 1 component (the group), NOT 3!
        canvas.Components.Count.ShouldBe(1,
            $"After redo group: should have 1 component (group), but found {canvas.Components.Count}. " +
            "Bug: CreateGroupCommand stored old ViewModels (vm1, vm2) but canvas has new ones (vm1New, vm2New)!");
    }

    /// <summary>
    /// Counts all nodes in the hierarchy tree (root nodes + all descendants)
    /// </summary>
    private int CountAllHierarchyNodes(HierarchyPanelViewModel hierarchy)
    {
        int count = 0;
        foreach (var rootNode in hierarchy.RootNodes)
        {
            count += CountNodeAndDescendants(rootNode);
        }
        return count;
    }

    /// <summary>
    /// Recursively counts a node and all its descendants
    /// </summary>
    private int CountNodeAndDescendants(HierarchyNodeViewModel node)
    {
        int count = 1; // Count this node
        foreach (var child in node.Children)
        {
            count += CountNodeAndDescendants(child);
        }
        return count;
    }
}
