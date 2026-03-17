using CAP.Avalonia.Commands;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Tests for the ToggleGroupLockCommand to ensure lock/unlock operations work correctly
/// and support undo/redo.
/// </summary>
// COMMENTED: ToggleGroupLockCommand deleted
/*
public class ToggleGroupLockCommandTests
{
    [Fact]
    public void Execute_UnlockedGroup_LocksTheGroup()
    {
        // Arrange
        var group = TestComponentFactory.CreateComponentGroup("TestGroup");
        group.IsLocked = false;

        var command = new ToggleGroupLockCommand(group);

        // Act
        command.Execute();

        // Assert
        group.IsLocked.ShouldBeTrue();
    }

    [Fact]
    public void Execute_LockedGroup_UnlocksTheGroup()
    {
        // Arrange
        var group = TestComponentFactory.CreateComponentGroup("TestGroup");
        group.IsLocked = true;

        var command = new ToggleGroupLockCommand(group);

        // Act
        command.Execute();

        // Assert
        group.IsLocked.ShouldBeFalse();
    }

    [Fact]
    public void Undo_AfterLocking_UnlocksTheGroup()
    {
        // Arrange
        var group = TestComponentFactory.CreateComponentGroup("TestGroup");
        group.IsLocked = false;

        var command = new ToggleGroupLockCommand(group);
        command.Execute();
        group.IsLocked.ShouldBeTrue();

        // Act
        command.Undo();

        // Assert
        group.IsLocked.ShouldBeFalse();
    }

    [Fact]
    public void Undo_AfterUnlocking_LocksTheGroup()
    {
        // Arrange
        var group = TestComponentFactory.CreateComponentGroup("TestGroup");
        group.IsLocked = true;

        var command = new ToggleGroupLockCommand(group);
        command.Execute();
        group.IsLocked.ShouldBeFalse();

        // Act
        command.Undo();

        // Assert
        group.IsLocked.ShouldBeTrue();
    }

    [Fact]
    public void Description_ForUnlockedGroup_IndicatesLock()
    {
        // Arrange
        var group = TestComponentFactory.CreateComponentGroup("MyTestGroup");
        group.IsLocked = false;

        var command = new ToggleGroupLockCommand(group);

        // Act & Assert
        command.Description.ShouldContain("Lock");
        command.Description.ShouldContain("MyTestGroup");
    }

    [Fact]
    public void Description_ForLockedGroup_IndicatesUnlock()
    {
        // Arrange
        var group = TestComponentFactory.CreateComponentGroup("MyTestGroup");
        group.IsLocked = true;

        var command = new ToggleGroupLockCommand(group);

        // Act & Assert
        command.Description.ShouldContain("Unlock");
        command.Description.ShouldContain("MyTestGroup");
    }

    [Fact]
    public void Constructor_NullGroup_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new ToggleGroupLockCommand(null!));
    }

    [Fact]
    public void ExecuteAndUndo_MultipleTimes_TogglesCorrectly()
    {
        // Arrange
        var group = TestComponentFactory.CreateComponentGroup("TestGroup");
        group.IsLocked = false;

        var command = new ToggleGroupLockCommand(group);

        // Act & Assert - Execute 1
        command.Execute();
        group.IsLocked.ShouldBeTrue();

        // Undo 1
        command.Undo();
        group.IsLocked.ShouldBeFalse();

        // Execute 2
        command.Execute();
        group.IsLocked.ShouldBeTrue();

        // Undo 2
        command.Undo();
        group.IsLocked.ShouldBeFalse();
    }
}
*/
