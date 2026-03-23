using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Tests to verify that component IDs/instances are preserved through undo/redo.
/// This is critical - if IDs change, the history points to the wrong components!
/// </summary>
public class GroupUndoComponentIdTests
{
    [Fact]
    public void CreateTwoComponents_GroupThem_UndoGroup_ComponentsShouldBeOriginalInstances()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        // Act 1: Create 2 components and remember their identifiers
        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        var comp1Id = comp1.Identifier;
        canvas.AddComponent(comp1);

        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.PhysicalX = 100;
        comp2.PhysicalY = 0;
        var comp2Id = comp2.Identifier;
        canvas.AddComponent(comp2);

        // Store original component references
        var originalComp1 = comp1;
        var originalComp2 = comp2;

        canvas.Components.Count.ShouldBe(2);

        // Act 2: Create group
        var vm1 = canvas.Components.First(c => c.Component == comp1);
        var vm2 = canvas.Components.First(c => c.Component == comp2);

        var createGroupCmd = new CreateGroupCommand(canvas, new List<ComponentViewModel> { vm1, vm2 });
        commandManager.ExecuteCommand(createGroupCmd);

        canvas.Components.Count(c => c.Component is ComponentGroup).ShouldBe(1);

        // Act 3: Undo group creation
        commandManager.Undo();

        // Assert: Components should be the ORIGINAL instances, not new clones!
        canvas.Components.Count.ShouldBe(2, "Should have 2 components after undo");

        var comp1AfterUndo = canvas.Components.FirstOrDefault(c => c.Component.Identifier == comp1Id)?.Component;
        var comp2AfterUndo = canvas.Components.FirstOrDefault(c => c.Component.Identifier == comp2Id)?.Component;

        comp1AfterUndo.ShouldNotBeNull("Component 1 should exist");
        comp2AfterUndo.ShouldNotBeNull("Component 2 should exist");

        // CRITICAL: These should be the SAME object references, not clones
        ReferenceEquals(comp1AfterUndo, originalComp1).ShouldBeTrue(
            "Component 1 after undo should be the SAME INSTANCE as original (not a clone with new ID)");
        ReferenceEquals(comp2AfterUndo, originalComp2).ShouldBeTrue(
            "Component 2 after undo should be the SAME INSTANCE as original (not a clone with new ID)");
    }

    [Fact]
    public void CreateTwoComponents_GroupThem_UndoAll_ShouldTrackCorrectInstances()
    {
        // This simulates the user scenario:
        // 1. Add component 1
        // 2. Add component 2
        // 3. Create group
        // 4. Undo (back to 2 components) <- Components have different IDs!
        // 5. Undo (should remove component 2) <- Fails because history points to old ID
        // 6. Undo (should remove component 1) <- Fails because history points to old ID

        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        // We're adding components manually (not via command) so there's nothing to undo for them
        // This mimics the real UI where components are added directly
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();

        canvas.AddComponent(comp1);
        canvas.AddComponent(comp2);

        var originalComp1Ref = comp1;
        var originalComp2Ref = comp2;

        // Act: Create group (this IS via command, so it's undoable)
        var vms = canvas.Components.ToList();
        var createGroupCmd = new CreateGroupCommand(canvas, vms);
        commandManager.ExecuteCommand(createGroupCmd);

        // Act: Undo group
        commandManager.Undo();

        // Assert: After undoing group creation, we should have the ORIGINAL component instances
        var comp1AfterUndo = canvas.Components.FirstOrDefault(c => c.Component == originalComp1Ref);
        var comp2AfterUndo = canvas.Components.FirstOrDefault(c => c.Component == originalComp2Ref);

        comp1AfterUndo.ShouldNotBeNull("Original component 1 instance should still exist");
        comp2AfterUndo.ShouldNotBeNull("Original component 2 instance should still exist");

        // The components on canvas should be the exact same object references as before grouping
        ReferenceEquals(comp1AfterUndo.Component, originalComp1Ref).ShouldBeTrue(
            "Component 1 should be the original instance, not a new clone");
        ReferenceEquals(comp2AfterUndo.Component, originalComp2Ref).ShouldBeTrue(
            "Component 2 should be the original instance, not a new clone");
    }

    [Fact]
    public void ComponentIdentifiers_ShouldNotChange_AfterGroupingAndUndo()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();

        canvas.AddComponent(comp1);
        canvas.AddComponent(comp2);

        var comp1IdBefore = comp1.Identifier;
        var comp2IdBefore = comp2.Identifier;

        // Act: Group and undo
        var vms = canvas.Components.ToList();
        var createGroupCmd = new CreateGroupCommand(canvas, vms);
        commandManager.ExecuteCommand(createGroupCmd);
        commandManager.Undo();

        // Assert: Identifiers should be unchanged
        var comp1AfterUndo = canvas.Components.FirstOrDefault(c => c.Component.Identifier == comp1IdBefore);
        var comp2AfterUndo = canvas.Components.FirstOrDefault(c => c.Component.Identifier == comp2IdBefore);

        comp1AfterUndo.ShouldNotBeNull($"Component with ID {comp1IdBefore} should exist");
        comp2AfterUndo.ShouldNotBeNull($"Component with ID {comp2IdBefore} should exist");

        comp1AfterUndo.Component.Identifier.ShouldBe(comp1IdBefore,
            "Component 1 identifier should not have changed");
        comp2AfterUndo.Component.Identifier.ShouldBe(comp2IdBefore,
            "Component 2 identifier should not have changed");
    }
}
