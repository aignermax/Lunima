using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
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
}
