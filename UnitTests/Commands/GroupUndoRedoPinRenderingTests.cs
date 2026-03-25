using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Tests to verify that group undo/redo doesn't create visual orphan components
/// or incorrect pin rendering issues.
/// </summary>
public class GroupUndoRedoPinRenderingTests
{
    [Fact]
    public void CreateGroup_UndoRedo_AllPinsCollectionShouldBeCorrect()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        // Create 2 components with physical pins
        var comp1 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        var vm1 = canvas.AddComponent(comp1);

        var comp2 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp2.PhysicalX = 100;
        comp2.PhysicalY = 0;
        var vm2 = canvas.AddComponent(comp2);

        int initialPinCount = canvas.AllPins.Count;
        initialPinCount.ShouldBeGreaterThan(0, "Should have pins for the 2 components");

        // Act 1: Create group
        var createGroupCmd = new CreateGroupCommand(
            canvas,
            new List<ComponentViewModel> { vm1, vm2 });
        commandManager.ExecuteCommand(createGroupCmd);

        // After group creation, AllPins should contain only group external pins
        int groupPinCount = canvas.AllPins.Count;
        canvas.Components.Count.ShouldBe(1, "Should have exactly 1 group");

        // Act 2: Undo
        commandManager.Undo();

        // After undo, AllPins should be back to original state
        canvas.AllPins.Count.ShouldBe(initialPinCount,
            $"After undo: AllPins should have {initialPinCount} pins (original state), but has {canvas.AllPins.Count}");
        canvas.Components.Count.ShouldBe(2, "After undo: Should have 2 components");

        // Act 3: Redo
        commandManager.Redo();

        // After redo, AllPins should match the group state (not have duplicates!)
        canvas.AllPins.Count.ShouldBe(groupPinCount,
            $"After redo: AllPins should have {groupPinCount} pins (group state), but has {canvas.AllPins.Count}");
        canvas.Components.Count.ShouldBe(1, "After redo: Should have exactly 1 group");

        // Verify no duplicate pins
        var pinSet = new HashSet<object>();
        foreach (var pinVm in canvas.AllPins)
        {
            pinSet.Add(pinVm).ShouldBeTrue("Each PinViewModel should be unique (no duplicates!)");
        }
    }

    [Fact]
    public void CreateGroup_UndoRedo_AllPinsShouldPointToCorrectParent()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        var comp1 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        var vm1 = canvas.AddComponent(comp1);

        var comp2 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp2.PhysicalX = 100;
        comp2.PhysicalY = 0;
        var vm2 = canvas.AddComponent(comp2);

        // Act: Create group
        var createGroupCmd = new CreateGroupCommand(
            canvas,
            new List<ComponentViewModel> { vm1, vm2 });
        commandManager.ExecuteCommand(createGroupCmd);

        var groupVm = canvas.Components.First(c => c.Component is ComponentGroup);

        // All pins should point to the group, not to child components
        foreach (var pinVm in canvas.AllPins)
        {
            pinVm.ParentComponentViewModel.ShouldBe(groupVm,
                "After group creation: All pins should have the group as parent");
        }

        // Act: Undo
        commandManager.Undo();

        // All pins should point to individual components
        foreach (var pinVm in canvas.AllPins)
        {
            pinVm.ParentComponentViewModel.ShouldNotBe(null, "After undo: Pins should have a parent");
            (pinVm.ParentComponentViewModel.Component is ComponentGroup).ShouldBeFalse(
                "After undo: Pins should NOT point to a group");
        }

        // Act: Redo
        commandManager.Redo();

        groupVm = canvas.Components.First(c => c.Component is ComponentGroup);

        // All pins should point to the group again
        foreach (var pinVm in canvas.AllPins)
        {
            pinVm.ParentComponentViewModel.ShouldBe(groupVm,
                "After redo: All pins should have the group as parent");
        }
    }

    [Fact]
    public void CreateGroup_UndoRedo_ComponentsShouldNotContainChildViewModels()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        var comp1 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        var vm1 = canvas.AddComponent(comp1);

        var comp2 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp2.PhysicalX = 100;
        comp2.PhysicalY = 0;
        var vm2 = canvas.AddComponent(comp2);

        // Remember the child component ViewModels
        var childViewModels = new HashSet<ComponentViewModel> { vm1, vm2 };

        // Act: Create group
        var createGroupCmd = new CreateGroupCommand(
            canvas,
            new List<ComponentViewModel> { vm1, vm2 });
        commandManager.ExecuteCommand(createGroupCmd);

        // Verify child ViewModels are NOT in Components collection
        foreach (var childVm in childViewModels)
        {
            canvas.Components.Contains(childVm).ShouldBeFalse(
                "After group creation: Child ComponentViewModels should NOT be in canvas.Components");
        }

        // Act: Undo
        commandManager.Undo();

        // Child ViewModels should be back in Components collection
        foreach (var childVm in childViewModels)
        {
            canvas.Components.Contains(childVm).ShouldBeTrue(
                "After undo: Child ComponentViewModels SHOULD be back in canvas.Components");
        }

        // Act: Redo
        commandManager.Redo();

        // Verify child ViewModels are NOT in Components collection again
        foreach (var childVm in childViewModels)
        {
            canvas.Components.Contains(childVm).ShouldBeFalse(
                "After redo: Child ComponentViewModels should NOT be in canvas.Components");
        }

        // Final verification: Only the group should be in Components
        canvas.Components.Count.ShouldBe(1, "Should have exactly 1 component (the group)");
        canvas.Components[0].Component.ShouldBeOfType<ComponentGroup>();
    }
}
