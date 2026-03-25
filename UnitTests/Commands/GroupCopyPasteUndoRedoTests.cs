using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Tests for issue #251: Group copy-paste with undo/redo creates shadow
/// components and wrong positions.
/// Verifies that paste/undo/redo cycles preserve object identity and positions.
/// </summary>
public class GroupCopyPasteUndoRedoTests
{
    [Fact]
    public void PasteGroup_UndoRedo_ShouldPreserveSameComponentInstances()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        var group = TestComponentFactory.CreateComponentGroup("TestGroup", addChildren: true);
        group.PhysicalX = 100;
        group.PhysicalY = 100;
        canvas.AddComponent(group);

        var groupVm = canvas.Components.First();
        canvas.Clipboard.Copy(new[] { groupVm }, canvas.Connections);

        // Act: Paste
        var pasteCmd = new PasteComponentsCommand(canvas, canvas.Clipboard);
        commandManager.ExecuteCommand(pasteCmd);
        var pastedComponent = pasteCmd.Result!.Components[0].Component;
        canvas.Components.Count.ShouldBe(2);

        // Act: Undo paste
        commandManager.Undo();
        canvas.Components.Count.ShouldBe(1);

        // Act: Redo paste
        commandManager.Redo();
        canvas.Components.Count.ShouldBe(2);

        // Assert: pasted component should be the SAME instance
        var redoneVm = canvas.Components.FirstOrDefault(c => c.Component == pastedComponent);
        redoneVm.ShouldNotBeNull("Redo should restore the same component instance");
    }

    [Fact]
    public void PasteGroup_UndoRedo_ShouldNotCreateShadowComponents()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        var group = TestComponentFactory.CreateComponentGroup("TestGroup", addChildren: true);
        group.PhysicalX = 100;
        group.PhysicalY = 100;
        canvas.AddComponent(group);

        var groupVm = canvas.Components.First();
        canvas.Clipboard.Copy(new[] { groupVm }, canvas.Connections);

        // Act: Paste → Undo → Redo → Undo → Redo (multiple cycles)
        var pasteCmd = new PasteComponentsCommand(canvas, canvas.Clipboard);
        commandManager.ExecuteCommand(pasteCmd);
        canvas.Components.Count.ShouldBe(2, "After paste: 2 components");

        commandManager.Undo();
        canvas.Components.Count.ShouldBe(1, "After undo: 1 component");

        commandManager.Redo();
        canvas.Components.Count.ShouldBe(2, "After first redo: 2 components");

        commandManager.Undo();
        canvas.Components.Count.ShouldBe(1, "After second undo: 1 component");

        commandManager.Redo();
        canvas.Components.Count.ShouldBe(2, "After second redo: 2 components (no shadow)");
    }

    [Fact]
    public void PasteGroup_MoveGroup_CreateSuperGroup_UndoAll_RedoAll_CorrectState()
    {
        // Reproduce the exact scenario from issue #251
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        // Step 1: Create two components
        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        var vm1 = canvas.AddComponent(comp1);

        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.PhysicalX = 300;
        comp2.PhysicalY = 0;
        var vm2 = canvas.AddComponent(comp2);

        // Step 2: Create a group
        var createGroupCmd = new CreateGroupCommand(
            canvas, new List<ComponentViewModel> { vm1, vm2 });
        commandManager.ExecuteCommand(createGroupCmd);
        canvas.Components.Count.ShouldBe(1, "Should have 1 group");

        var groupVm = canvas.Components.First();
        var group1 = (ComponentGroup)groupVm.Component;

        // Step 3: Copy the group
        canvas.Clipboard.Copy(new[] { groupVm }, canvas.Connections);

        // Step 4: Paste the group
        var pasteCmd = new PasteComponentsCommand(canvas, canvas.Clipboard);
        commandManager.ExecuteCommand(pasteCmd);
        canvas.Components.Count.ShouldBe(2, "Should have 2 groups after paste");

        var pastedGroupVm = pasteCmd.Result!.Components[0];
        var group2 = (ComponentGroup)pastedGroupVm.Component;

        // Step 5: Move the pasted group
        double moveEndX = pastedGroupVm.X + 200;
        double moveEndY = pastedGroupVm.Y + 200;
        var moveCmd = new MoveComponentCommand(
            canvas, pastedGroupVm,
            pastedGroupVm.X, pastedGroupVm.Y,
            moveEndX, moveEndY);
        commandManager.ExecuteCommand(moveCmd);

        // Step 6: Create super-group from both groups
        var updatedGroupVm = canvas.Components.First(c => c.Component == group1);
        var updatedPastedVm = canvas.Components.First(c => c.Component == group2);
        var superGroupCmd = new CreateGroupCommand(
            canvas, new List<ComponentViewModel> { updatedGroupVm, updatedPastedVm });
        commandManager.ExecuteCommand(superGroupCmd);
        canvas.Components.Count.ShouldBe(1, "Should have 1 super-group");

        // Step 7: Undo 4 times (super-group, move, paste, group)
        commandManager.Undo(); // undo super-group
        canvas.Components.Count.ShouldBe(2, "After undo super-group: 2 groups");

        commandManager.Undo(); // undo move
        canvas.Components.Count.ShouldBe(2, "After undo move: 2 groups");

        commandManager.Undo(); // undo paste
        canvas.Components.Count.ShouldBe(1, "After undo paste: 1 group");

        commandManager.Undo(); // undo group creation
        canvas.Components.Count.ShouldBe(2, "After undo group: 2 components");

        // Step 8: Redo 4 times
        commandManager.Redo(); // redo group creation
        canvas.Components.Count.ShouldBe(1, "After redo group: 1 group");

        commandManager.Redo(); // redo paste
        canvas.Components.Count.ShouldBe(2, "After redo paste: 2 groups");

        commandManager.Redo(); // redo move
        canvas.Components.Count.ShouldBe(2, "After redo move: 2 groups");

        commandManager.Redo(); // redo super-group
        canvas.Components.Count.ShouldBe(1, "After redo super-group: 1 super-group");
    }

    [Fact]
    public void PasteGroup_UndoRedo_ShouldPreservePositions()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        var group = TestComponentFactory.CreateComponentGroup("TestGroup", addChildren: true);
        group.PhysicalX = 100;
        group.PhysicalY = 100;
        canvas.AddComponent(group);

        var groupVm = canvas.Components.First();
        canvas.Clipboard.Copy(new[] { groupVm }, canvas.Connections);

        // Act: Paste
        var pasteCmd = new PasteComponentsCommand(canvas, canvas.Clipboard);
        commandManager.ExecuteCommand(pasteCmd);
        var pastedVm = pasteCmd.Result!.Components[0];
        double pasteX = pastedVm.X;
        double pasteY = pastedVm.Y;

        // Act: Undo then Redo
        commandManager.Undo();
        commandManager.Redo();

        // Assert: position should be preserved
        var restoredVm = canvas.Components.First(c => c.Component == pastedVm.Component);
        restoredVm.X.ShouldBe(pasteX, "X position should be preserved after undo/redo");
        restoredVm.Y.ShouldBe(pasteY, "Y position should be preserved after undo/redo");
    }

    [Fact]
    public void CreateGroup_UndoRedo_ShouldSyncViewModelPositions()
    {
        // Tests that VM.X/Y are synced with Component.PhysicalX/Y on undo
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 50;
        comp1.PhysicalY = 50;
        var vm1 = canvas.AddComponent(comp1);

        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.PhysicalX = 350;
        comp2.PhysicalY = 50;
        var vm2 = canvas.AddComponent(comp2);

        double originalX1 = vm1.X;
        double originalY1 = vm1.Y;
        double originalX2 = vm2.X;
        double originalY2 = vm2.Y;

        // Create group
        var createGroupCmd = new CreateGroupCommand(
            canvas, new List<ComponentViewModel> { vm1, vm2 });
        commandManager.ExecuteCommand(createGroupCmd);

        // Undo
        commandManager.Undo();

        // Assert: VM positions should match original positions
        var restoredVm1 = canvas.Components.First(c => c.Component == comp1);
        var restoredVm2 = canvas.Components.First(c => c.Component == comp2);

        restoredVm1.X.ShouldBe(originalX1, "VM1.X should match original after undo");
        restoredVm1.Y.ShouldBe(originalY1, "VM1.Y should match original after undo");
        restoredVm2.X.ShouldBe(originalX2, "VM2.X should match original after undo");
        restoredVm2.Y.ShouldBe(originalY2, "VM2.Y should match original after undo");

        // Also verify model is synced
        restoredVm1.Component.PhysicalX.ShouldBe(restoredVm1.X,
            "Model PhysicalX should match VM X");
        restoredVm2.Component.PhysicalX.ShouldBe(restoredVm2.X,
            "Model PhysicalX should match VM X");
    }

    [Fact]
    public void UngroupCommand_UndoRedo_ShouldPreserveViewModelIdentity()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        var vm1 = canvas.AddComponent(comp1);

        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.PhysicalX = 300;
        comp2.PhysicalY = 0;
        var vm2 = canvas.AddComponent(comp2);

        // Create group
        var createGroupCmd = new CreateGroupCommand(
            canvas, new List<ComponentViewModel> { vm1, vm2 });
        commandManager.ExecuteCommand(createGroupCmd);

        var group = (ComponentGroup)canvas.Components.First().Component;

        // Ungroup
        var ungroupCmd = new UngroupCommand(canvas, group);
        commandManager.ExecuteCommand(ungroupCmd);
        canvas.Components.Count.ShouldBe(2, "After ungroup: 2 components");

        var childVms = canvas.Components.ToList();

        // Undo ungroup
        commandManager.Undo();
        canvas.Components.Count.ShouldBe(1, "After undo ungroup: 1 group");

        // Redo ungroup
        commandManager.Redo();
        canvas.Components.Count.ShouldBe(2, "After redo ungroup: 2 components");

        // Assert: same child component instances should be restored
        foreach (var childVm in childVms)
        {
            canvas.Components.ShouldContain(c => c.Component == childVm.Component,
                "Redo should restore the same component instance");
        }
    }

    [Fact]
    public void UngroupCommand_MultipleUndoRedoCycles_NoShadowComponents()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        canvas.AddComponent(comp1);

        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.PhysicalX = 300;
        comp2.PhysicalY = 0;
        canvas.AddComponent(comp2);

        // Create group
        var vms = canvas.Components.ToList();
        var createGroupCmd = new CreateGroupCommand(canvas, vms);
        commandManager.ExecuteCommand(createGroupCmd);
        var group = (ComponentGroup)canvas.Components.First().Component;

        // Ungroup
        var ungroupCmd = new UngroupCommand(canvas, group);
        commandManager.ExecuteCommand(ungroupCmd);

        // Multiple undo/redo cycles
        for (int i = 0; i < 3; i++)
        {
            commandManager.Undo(); // undo ungroup → 1 group
            canvas.Components.Count.ShouldBe(1, $"Cycle {i}: After undo ungroup: 1 group");

            commandManager.Redo(); // redo ungroup → 2 components
            canvas.Components.Count.ShouldBe(2, $"Cycle {i}: After redo ungroup: 2 components");
        }
    }
}
