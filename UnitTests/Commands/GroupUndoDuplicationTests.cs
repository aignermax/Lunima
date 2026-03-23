using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Tests to verify that undoing group creation doesn't duplicate components.
/// This is a critical bug where components appear twice after undo.
/// </summary>
public class GroupUndoDuplicationTests
{
    [Fact]
    public void CreateGroup_ThenUndo_ShouldHaveExactly3Components()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        // Act 1: Create 3 components
        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        canvas.AddComponent(comp1);

        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.PhysicalX = 100;
        comp2.PhysicalY = 0;
        canvas.AddComponent(comp2);

        var comp3 = TestComponentFactory.CreateBasicComponent();
        comp3.PhysicalX = 200;
        comp3.PhysicalY = 0;
        canvas.AddComponent(comp3);

        // Verify: 3 components
        canvas.Components.Count.ShouldBe(3, "Should have exactly 3 components after adding them");

        // Act 2: Create group from all 3 components
        var vm1 = canvas.Components.First(c => c.Component == comp1);
        var vm2 = canvas.Components.First(c => c.Component == comp2);
        var vm3 = canvas.Components.First(c => c.Component == comp3);

        var createGroupCmd = new CreateGroupCommand(
            canvas,
            new List<ComponentViewModel> { vm1, vm2, vm3 });
        commandManager.ExecuteCommand(createGroupCmd);

        // Verify: Should have group + (possibly) children depending on implementation
        var groupCount = canvas.Components.Count(c => c.Component is ComponentGroup);
        groupCount.ShouldBe(1, "Should have exactly 1 group");

        // Act 3: Undo group creation
        commandManager.Undo();

        // Assert: CRITICAL - Should have EXACTLY 3 components, NO DUPLICATES
        canvas.Components.Count.ShouldBe(3,
            "After undo, should have exactly 3 components (not 6 due to duplication!)");

        var componentCount = canvas.Components.Count(c => c.Component is not ComponentGroup);
        componentCount.ShouldBe(3, "Should have 3 non-group components");

        canvas.Components.Count(c => c.Component is ComponentGroup).ShouldBe(0,
            "Should have 0 groups after undo");

        // Verify components are the original ones (not duplicates)
        canvas.Components.Count(c => c.Component == comp1).ShouldBe(1,
            "Component 1 should appear exactly once");
        canvas.Components.Count(c => c.Component == comp2).ShouldBe(1,
            "Component 2 should appear exactly once");
        canvas.Components.Count(c => c.Component == comp3).ShouldBe(1,
            "Component 3 should appear exactly once");
    }

    [Fact]
    public void CreateGroup_ThenUndo_ComponentsShouldNotBeDuplicated()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        var comp3 = TestComponentFactory.CreateBasicComponent();

        canvas.AddComponent(comp1);
        canvas.AddComponent(comp2);
        canvas.AddComponent(comp3);

        var originalComponentVMs = canvas.Components.ToList();
        originalComponentVMs.Count.ShouldBe(3);

        // Act: Create group
        var createGroupCmd = new CreateGroupCommand(canvas, originalComponentVMs);
        commandManager.ExecuteCommand(createGroupCmd);

        // Act: Undo
        commandManager.Undo();

        // Assert: Check for duplicates by component reference
        var component1Count = canvas.Components.Count(c => c.Component == comp1);
        var component2Count = canvas.Components.Count(c => c.Component == comp2);
        var component3Count = canvas.Components.Count(c => c.Component == comp3);

        component1Count.ShouldBe(1, $"Component 1 appears {component1Count} times but should be 1");
        component2Count.ShouldBe(1, $"Component 2 appears {component2Count} times but should be 1");
        component3Count.ShouldBe(1, $"Component 3 appears {component3Count} times but should be 1");

        // Total count check
        canvas.Components.Count.ShouldBe(3,
            $"Canvas has {canvas.Components.Count} components but should have exactly 3");
    }

    [Fact]
    public void CreateGroup_ThenUndoRedo_ShouldMaintainCorrectCount()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        var comp3 = TestComponentFactory.CreateBasicComponent();

        canvas.AddComponent(comp1);
        canvas.AddComponent(comp2);
        canvas.AddComponent(comp3);

        var vms = canvas.Components.ToList();

        // Act: Create group
        var createGroupCmd = new CreateGroupCommand(canvas, vms);
        commandManager.ExecuteCommand(createGroupCmd);

        // Act: Undo
        commandManager.Undo();
        canvas.Components.Count.ShouldBe(3, "After first undo: should have 3 components");

        // Act: Redo
        commandManager.Redo();
        canvas.Components.Count(c => c.Component is ComponentGroup).ShouldBe(1,
            "After redo: should have 1 group");

        // Act: Undo again
        commandManager.Undo();
        canvas.Components.Count.ShouldBe(3, "After second undo: should have 3 components again");
    }
}
