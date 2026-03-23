using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Tests for CommandManager to ensure undo/redo history works correctly.
/// </summary>
public class CommandManagerTests
{
    [Fact]
    public void ExecuteCommand_AddsToUndoStack_CanUndoIsTrue()
    {
        // Arrange
        var commandManager = new CommandManager();
        var canvas = new DesignCanvasViewModel();
        var component = CreateTestComponent("TestComp", 100, 200);
        var vm = canvas.AddComponent(component, "TestTemplate");
        var cmd = new MoveComponentCommand(canvas, vm, 100, 200, 150, 250);

        // Act
        commandManager.ExecuteCommand(cmd);

        // Assert
        commandManager.CanUndo.ShouldBeTrue("Should be able to undo after executing a command");
        commandManager.UndoCount.ShouldBe(1, "Undo stack should contain 1 command");
        commandManager.CanRedo.ShouldBeFalse("Should not be able to redo before undoing");
        commandManager.RedoCount.ShouldBe(0, "Redo stack should be empty");
    }

    [Fact]
    public void ExecuteCommand_MultipleCommands_BuildsHistory()
    {
        // Arrange
        var commandManager = new CommandManager();
        var canvas = new DesignCanvasViewModel();
        var component1 = CreateTestComponent("Comp1", 100, 200);
        var vm1 = canvas.AddComponent(component1, "TestTemplate");
        var component2 = CreateTestComponent("Comp2", 300, 400);
        var vm2 = canvas.AddComponent(component2, "TestTemplate");

        // Act - Execute 3 commands
        commandManager.ExecuteCommand(new MoveComponentCommand(canvas, vm1, 100, 200, 150, 250));
        commandManager.ExecuteCommand(new MoveComponentCommand(canvas, vm2, 300, 400, 350, 450));
        commandManager.ExecuteCommand(new DeleteComponentCommand(canvas, vm1));

        // Assert
        commandManager.CanUndo.ShouldBeTrue();
        commandManager.UndoCount.ShouldBe(3, "Should have 3 commands in history");
        commandManager.CanRedo.ShouldBeFalse();
        commandManager.RedoCount.ShouldBe(0);
    }

    [Fact]
    public void Undo_AfterExecute_MovesToRedoStack()
    {
        // Arrange
        var commandManager = new CommandManager();
        var canvas = new DesignCanvasViewModel();
        var component = CreateTestComponent("TestComp", 100, 200);
        var vm = canvas.AddComponent(component, "TestTemplate");
        var cmd = new MoveComponentCommand(canvas, vm, 100, 200, 150, 250);
        commandManager.ExecuteCommand(cmd);

        // Act
        var result = commandManager.Undo();

        // Assert
        result.ShouldBeTrue("Undo should return true when successful");
        commandManager.CanUndo.ShouldBeFalse("Should not be able to undo after undoing the only command");
        commandManager.UndoCount.ShouldBe(0, "Undo stack should be empty");
        commandManager.CanRedo.ShouldBeTrue("Should be able to redo after undoing");
        commandManager.RedoCount.ShouldBe(1, "Redo stack should contain 1 command");
    }

    [Fact]
    public void Redo_AfterUndo_RestoresCommand()
    {
        // Arrange
        var commandManager = new CommandManager();
        var canvas = new DesignCanvasViewModel();
        var component = CreateTestComponent("TestComp", 100, 200);
        var vm = canvas.AddComponent(component, "TestTemplate");
        var cmd = new MoveComponentCommand(canvas, vm, 100, 200, 150, 250);
        commandManager.ExecuteCommand(cmd);
        commandManager.Undo();

        // Act
        var result = commandManager.Redo();

        // Assert
        result.ShouldBeTrue("Redo should return true when successful");
        commandManager.CanUndo.ShouldBeTrue("Should be able to undo after redoing");
        commandManager.UndoCount.ShouldBe(1, "Undo stack should contain 1 command");
        commandManager.CanRedo.ShouldBeFalse("Should not be able to redo after redoing the only command");
        commandManager.RedoCount.ShouldBe(0, "Redo stack should be empty");
    }

    [Fact]
    public void ExecuteCommand_AfterUndo_ClearsRedoStack()
    {
        // Arrange - Use DeleteCommand to avoid command merging
        var commandManager = new CommandManager();
        var canvas = new DesignCanvasViewModel();
        var component1 = CreateTestComponent("Comp1", 100, 200);
        var vm1 = canvas.AddComponent(component1, "TestTemplate");
        var component2 = CreateTestComponent("Comp2", 200, 300);
        var vm2 = canvas.AddComponent(component2, "TestTemplate");

        commandManager.ExecuteCommand(new DeleteComponentCommand(canvas, vm1));
        commandManager.ExecuteCommand(new DeleteComponentCommand(canvas, vm2));
        commandManager.Undo(); // Undo the second delete (restore vm2)

        canvas.Components.Count.ShouldBe(1, "Should have 1 component after undo");

        // Act - Execute a new command (should clear redo stack)
        var component3 = CreateTestComponent("Comp3", 300, 400);
        var vm3 = canvas.AddComponent(component3, "TestTemplate");
        commandManager.ExecuteCommand(new DeleteComponentCommand(canvas, vm3));

        // Assert
        commandManager.CanRedo.ShouldBeFalse("Redo stack should be cleared when new command is executed");
        commandManager.RedoCount.ShouldBe(0, "Redo stack should be empty");
        commandManager.CanUndo.ShouldBeTrue();
        commandManager.UndoCount.ShouldBe(2, "Should have 2 commands in undo stack");
    }

    [Fact]
    public void UndoRedo_MultipleDeletes_WorksCorrectly()
    {
        // Arrange - Use DeleteCommand to avoid command merging
        var commandManager = new CommandManager();
        var canvas = new DesignCanvasViewModel();
        var component1 = CreateTestComponent("Comp1", 100, 200);
        var vm1 = canvas.AddComponent(component1, "TestTemplate");
        var component2 = CreateTestComponent("Comp2", 200, 300);
        var vm2 = canvas.AddComponent(component2, "TestTemplate");
        var component3 = CreateTestComponent("Comp3", 300, 400);
        var vm3 = canvas.AddComponent(component3, "TestTemplate");

        // Execute 3 delete commands
        commandManager.ExecuteCommand(new DeleteComponentCommand(canvas, vm1));
        commandManager.ExecuteCommand(new DeleteComponentCommand(canvas, vm2));
        commandManager.ExecuteCommand(new DeleteComponentCommand(canvas, vm3));

        canvas.Components.Count.ShouldBe(0, "All components should be deleted");

        // Act - Undo twice
        commandManager.Undo();
        commandManager.Undo();

        // Assert after 2 undos
        commandManager.UndoCount.ShouldBe(1, "Should have 1 command left in undo stack");
        commandManager.RedoCount.ShouldBe(2, "Should have 2 commands in redo stack");
        canvas.Components.Count.ShouldBe(2, "Should have 2 components restored");

        // Act - Redo once
        commandManager.Redo();

        // Assert after 1 redo
        commandManager.UndoCount.ShouldBe(2, "Should have 2 commands in undo stack");
        commandManager.RedoCount.ShouldBe(1, "Should have 1 command in redo stack");
        canvas.Components.Count.ShouldBe(1, "Should have 1 component after redo");
    }

    [Fact]
    public void Undo_WhenEmpty_ReturnsFalse()
    {
        // Arrange
        var commandManager = new CommandManager();

        // Act
        var result = commandManager.Undo();

        // Assert
        result.ShouldBeFalse("Undo should return false when history is empty");
        commandManager.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void Redo_WhenEmpty_ReturnsFalse()
    {
        // Arrange
        var commandManager = new CommandManager();

        // Act
        var result = commandManager.Redo();

        // Assert
        result.ShouldBeFalse("Redo should return false when redo stack is empty");
        commandManager.CanRedo.ShouldBeFalse();
    }

    [Fact]
    public void ClearHistory_RemovesAllCommands()
    {
        // Arrange
        var commandManager = new CommandManager();
        var canvas = new DesignCanvasViewModel();
        var component = CreateTestComponent("TestComp", 100, 200);
        var vm = canvas.AddComponent(component, "TestTemplate");
        commandManager.ExecuteCommand(new MoveComponentCommand(canvas, vm, 100, 200, 150, 250));
        commandManager.Undo();

        // Act
        commandManager.ClearHistory();

        // Assert
        commandManager.CanUndo.ShouldBeFalse("Should not be able to undo after clearing history");
        commandManager.CanRedo.ShouldBeFalse("Should not be able to redo after clearing history");
        commandManager.UndoCount.ShouldBe(0);
        commandManager.RedoCount.ShouldBe(0);
    }

    [Fact]
    public void PropertyChanged_RaisedOnExecute()
    {
        // Arrange
        var commandManager = new CommandManager();
        var canvas = new DesignCanvasViewModel();
        var component = CreateTestComponent("TestComp", 100, 200);
        var vm = canvas.AddComponent(component, "TestTemplate");
        var cmd = new MoveComponentCommand(canvas, vm, 100, 200, 150, 250);

        var propertyChangedRaised = false;
        commandManager.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CommandManager.CanUndo))
                propertyChangedRaised = true;
        };

        // Act
        commandManager.ExecuteCommand(cmd);

        // Assert
        propertyChangedRaised.ShouldBeTrue("PropertyChanged should be raised when command is executed");
    }

    [Fact]
    public void StateChanged_RaisedOnExecute()
    {
        // Arrange
        var commandManager = new CommandManager();
        var canvas = new DesignCanvasViewModel();
        var component = CreateTestComponent("TestComp", 100, 200);
        var vm = canvas.AddComponent(component, "TestTemplate");
        var cmd = new MoveComponentCommand(canvas, vm, 100, 200, 150, 250);

        var stateChangedRaised = false;
        commandManager.StateChanged += (s, e) => stateChangedRaised = true;

        // Act
        commandManager.ExecuteCommand(cmd);

        // Assert
        stateChangedRaised.ShouldBeTrue("StateChanged should be raised when command is executed");
    }

    private static Component CreateTestComponent(string identifier, double x, double y)
    {
        var component = TestComponentFactory.CreateBasicComponent();
        component.PhysicalX = x;
        component.PhysicalY = y;
        return component;
    }
}
