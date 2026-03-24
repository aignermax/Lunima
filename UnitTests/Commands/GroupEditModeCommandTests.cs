using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.Commands;

/// <summary>
/// Tests for EnterGroupEditModeCommand and ExitGroupEditModeCommand
/// verifying undo/redo integration with group edit mode.
/// </summary>
public class GroupEditModeCommandTests
{
    [Fact]
    public void EnterCommand_Execute_EntersGroupEditMode()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var group = TestComponentFactory.CreateComponentGroup("TestGroup", addChildren: true);
        canvas.AddComponent(group);
        var cmd = new EnterGroupEditModeCommand(canvas, group);

        // Act
        cmd.Execute();

        // Assert
        canvas.CurrentEditGroup.ShouldBe(group);
        canvas.IsInGroupEditMode.ShouldBeTrue();
    }

    [Fact]
    public void EnterCommand_Undo_ExitsGroupEditMode()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var group = TestComponentFactory.CreateComponentGroup("TestGroup", addChildren: true);
        canvas.AddComponent(group);
        var cmd = new EnterGroupEditModeCommand(canvas, group);
        cmd.Execute();

        // Act
        cmd.Undo();

        // Assert
        canvas.CurrentEditGroup.ShouldBeNull();
        canvas.IsInGroupEditMode.ShouldBeFalse();
    }

    [Fact]
    public void EnterCommand_Description_ContainsGroupName()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var group = new ComponentGroup("MyGroupName");
        var cmd = new EnterGroupEditModeCommand(canvas, group);

        // Assert
        cmd.Description.ShouldContain("MyGroupName");
        cmd.Description.ShouldContain("Enter group edit mode");
    }

    [Fact]
    public void ExitCommand_Execute_ExitsGroupEditMode()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var group = TestComponentFactory.CreateComponentGroup("TestGroup", addChildren: true);
        canvas.AddComponent(group);
        canvas.EnterGroupEditMode(group);
        var cmd = new ExitGroupEditModeCommand(canvas, group);

        // Act
        cmd.Execute();

        // Assert
        canvas.CurrentEditGroup.ShouldBeNull();
        canvas.IsInGroupEditMode.ShouldBeFalse();
    }

    [Fact]
    public void ExitCommand_Undo_ReEntersGroupEditMode()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var group = TestComponentFactory.CreateComponentGroup("TestGroup", addChildren: true);
        canvas.AddComponent(group);
        canvas.EnterGroupEditMode(group);
        var cmd = new ExitGroupEditModeCommand(canvas, group);
        cmd.Execute();

        // Act
        cmd.Undo();

        // Assert
        canvas.CurrentEditGroup.ShouldBe(group);
        canvas.IsInGroupEditMode.ShouldBeTrue();
    }

    [Fact]
    public void ExitCommand_Description_ContainsGroupName()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var group = new ComponentGroup("MyGroupName");
        var cmd = new ExitGroupEditModeCommand(canvas, group);

        // Assert
        cmd.Description.ShouldContain("MyGroupName");
        cmd.Description.ShouldContain("Exit group edit mode");
    }

    [Fact]
    public void EnterCommand_ViaCommandManager_CanUndo()
    {
        // Arrange
        var commandManager = new CommandManager();
        var canvas = new DesignCanvasViewModel();
        var group = TestComponentFactory.CreateComponentGroup("TestGroup", addChildren: true);
        canvas.AddComponent(group);

        // Act
        var cmd = new EnterGroupEditModeCommand(canvas, group);
        commandManager.ExecuteCommand(cmd);

        // Assert
        commandManager.CanUndo.ShouldBeTrue();
        canvas.IsInGroupEditMode.ShouldBeTrue();
    }

    [Fact]
    public void EnterCommand_UndoViaCommandManager_ExitsEditMode()
    {
        // Arrange
        var commandManager = new CommandManager();
        var canvas = new DesignCanvasViewModel();
        var group = TestComponentFactory.CreateComponentGroup("TestGroup", addChildren: true);
        canvas.AddComponent(group);
        commandManager.ExecuteCommand(new EnterGroupEditModeCommand(canvas, group));

        // Act
        commandManager.Undo();

        // Assert
        canvas.IsInGroupEditMode.ShouldBeFalse();
        canvas.CurrentEditGroup.ShouldBeNull();
    }

    [Fact]
    public void EnterCommand_RedoViaCommandManager_ReEntersEditMode()
    {
        // Arrange
        var commandManager = new CommandManager();
        var canvas = new DesignCanvasViewModel();
        var group = TestComponentFactory.CreateComponentGroup("TestGroup", addChildren: true);
        canvas.AddComponent(group);
        commandManager.ExecuteCommand(new EnterGroupEditModeCommand(canvas, group));
        commandManager.Undo();

        // Act
        commandManager.Redo();

        // Assert
        canvas.IsInGroupEditMode.ShouldBeTrue();
        canvas.CurrentEditGroup.ShouldBe(group);
    }

    [Fact]
    public void ExitCommand_ViaCommandManager_UndoReEnters()
    {
        // Arrange
        var commandManager = new CommandManager();
        var canvas = new DesignCanvasViewModel();
        var group = TestComponentFactory.CreateComponentGroup("TestGroup", addChildren: true);
        canvas.AddComponent(group);
        commandManager.ExecuteCommand(new EnterGroupEditModeCommand(canvas, group));
        commandManager.ExecuteCommand(new ExitGroupEditModeCommand(canvas, group));

        // Act
        commandManager.Undo();

        // Assert — undo of exit should re-enter
        canvas.IsInGroupEditMode.ShouldBeTrue();
        canvas.CurrentEditGroup.ShouldBe(group);
    }

    [Fact]
    public void EnterThenExit_UndoChain_WorksCorrectly()
    {
        // Arrange
        var commandManager = new CommandManager();
        var canvas = new DesignCanvasViewModel();
        var group = TestComponentFactory.CreateComponentGroup("TestGroup", addChildren: true);
        canvas.AddComponent(group);

        // Enter edit mode
        commandManager.ExecuteCommand(new EnterGroupEditModeCommand(canvas, group));
        canvas.IsInGroupEditMode.ShouldBeTrue();

        // Exit edit mode
        commandManager.ExecuteCommand(new ExitGroupEditModeCommand(canvas, group));
        canvas.IsInGroupEditMode.ShouldBeFalse();

        // Act — Undo exit (should re-enter)
        commandManager.Undo();
        canvas.IsInGroupEditMode.ShouldBeTrue();
        canvas.CurrentEditGroup.ShouldBe(group);

        // Act — Undo enter (should exit)
        commandManager.Undo();
        canvas.IsInGroupEditMode.ShouldBeFalse();
        canvas.CurrentEditGroup.ShouldBeNull();

        // Assert — both undone
        commandManager.UndoCount.ShouldBe(0);
        commandManager.RedoCount.ShouldBe(2);
    }

    [Fact]
    public void NestedGroups_EnterTwice_UndoTwice_ExitsBoth()
    {
        // Arrange
        var commandManager = new CommandManager();
        var canvas = new DesignCanvasViewModel();
        var outerGroup = TestComponentFactory.CreateComponentGroup("Outer");
        var innerGroup = new ComponentGroup("Inner");
        outerGroup.AddChild(innerGroup);
        canvas.AddComponent(outerGroup);

        // Enter outer group
        commandManager.ExecuteCommand(new EnterGroupEditModeCommand(canvas, outerGroup));
        canvas.CurrentEditGroup.ShouldBe(outerGroup);

        // Enter inner group
        commandManager.ExecuteCommand(new EnterGroupEditModeCommand(canvas, innerGroup));
        canvas.CurrentEditGroup.ShouldBe(innerGroup);

        // Act — Undo inner enter (exits inner, returns to outer)
        commandManager.Undo();
        canvas.CurrentEditGroup.ShouldBe(outerGroup);
        canvas.IsInGroupEditMode.ShouldBeTrue();

        // Act — Undo outer enter (exits outer, returns to root)
        commandManager.Undo();
        canvas.CurrentEditGroup.ShouldBeNull();
        canvas.IsInGroupEditMode.ShouldBeFalse();
    }

    [Fact]
    public void CommandHistory_PreservesGroupContext()
    {
        // Arrange
        var commandManager = new CommandManager();
        var canvas = new DesignCanvasViewModel();
        var group = TestComponentFactory.CreateComponentGroup("SpecificGroup", addChildren: true);
        canvas.AddComponent(group);

        // Act
        commandManager.ExecuteCommand(new EnterGroupEditModeCommand(canvas, group));

        // Assert — description should identify the group
        commandManager.UndoDescription.ShouldContain("SpecificGroup");
    }

    [Fact]
    public void EnterCommand_NullCanvas_ThrowsArgumentNullException()
    {
        // Assert
        Should.Throw<ArgumentNullException>(() =>
            new EnterGroupEditModeCommand(null!, new ComponentGroup("Test")));
    }

    [Fact]
    public void EnterCommand_NullGroup_ThrowsArgumentNullException()
    {
        // Assert
        Should.Throw<ArgumentNullException>(() =>
            new EnterGroupEditModeCommand(new DesignCanvasViewModel(), null!));
    }

    [Fact]
    public void ExitCommand_NullCanvas_ThrowsArgumentNullException()
    {
        // Assert
        Should.Throw<ArgumentNullException>(() =>
            new ExitGroupEditModeCommand(null!, new ComponentGroup("Test")));
    }

    [Fact]
    public void ExitCommand_NullGroup_ThrowsArgumentNullException()
    {
        // Assert
        Should.Throw<ArgumentNullException>(() =>
            new ExitGroupEditModeCommand(new DesignCanvasViewModel(), null!));
    }
}
