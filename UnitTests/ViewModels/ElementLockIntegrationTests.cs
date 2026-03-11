using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.Commands;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Integration tests for the ElementLockViewModel with DesignCanvasViewModel.
/// Tests the complete lock/unlock workflow from ViewModel to Core.
/// </summary>
public class ElementLockIntegrationTests
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ElementLockViewModel _lockViewModel;
    private readonly CommandManager _commandManager;

    public ElementLockIntegrationTests()
    {
        _canvas = new DesignCanvasViewModel();
        _lockViewModel = new ElementLockViewModel();
        _commandManager = new CommandManager();
        _lockViewModel.Configure(_canvas, _commandManager);
    }

    [Fact]
    public void LockSelectedComponents_LocksComponentsInCanvas()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 500;
        comp2.PhysicalY = 0;

        var vm1 = _canvas.AddComponent(comp1, "Test1");
        var vm2 = _canvas.AddComponent(comp2, "Test2");

        _canvas.Selection.AddToSelection(vm1);
        _canvas.Selection.AddToSelection(vm2);

        // Act
        _lockViewModel.LockSelectedComponentsCommand.Execute(null);

        // Assert
        comp1.IsLocked.ShouldBeTrue();
        comp2.IsLocked.ShouldBeTrue();
        _lockViewModel.LockedComponentCount.ShouldBe(2);
    }

    [Fact]
    public void UnlockSelectedComponents_UnlocksComponentsInCanvas()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 500;
        comp2.PhysicalY = 0;
        comp1.IsLocked = true;
        comp2.IsLocked = true;

        var vm1 = _canvas.AddComponent(comp1, "Test1");
        var vm2 = _canvas.AddComponent(comp2, "Test2");

        _canvas.Selection.AddToSelection(vm1);
        _canvas.Selection.AddToSelection(vm2);

        // Act
        _lockViewModel.UnlockSelectedComponentsCommand.Execute(null);

        // Assert
        comp1.IsLocked.ShouldBeFalse();
        comp2.IsLocked.ShouldBeFalse();
        _lockViewModel.LockedComponentCount.ShouldBe(0);
    }

    [Fact]
    public void ToggleSelectedComponents_LocksAllWhenAnyUnlocked()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 500;
        comp2.PhysicalY = 0;
        comp1.IsLocked = false;
        comp2.IsLocked = true;

        var vm1 = _canvas.AddComponent(comp1, "Test1");
        var vm2 = _canvas.AddComponent(comp2, "Test2");

        _canvas.Selection.AddToSelection(vm1);
        _canvas.Selection.AddToSelection(vm2);

        // Act - since comp1 is unlocked, should lock it
        _lockViewModel.ToggleSelectedComponentsCommand.Execute(null);

        // Assert - comp1 should now be locked, comp2 unchanged
        comp1.IsLocked.ShouldBeTrue();
        comp2.IsLocked.ShouldBeTrue();
    }

    [Fact]
    public void UnlockAllComponents_UnlocksAllComponentsInCanvas()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        var comp3 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 500;
        comp2.PhysicalY = 0;
        comp3.PhysicalX = 1000;
        comp3.PhysicalY = 0;

        comp1.IsLocked = true;
        comp2.IsLocked = true;
        comp3.IsLocked = true;

        _canvas.AddComponent(comp1, "Test1");
        _canvas.AddComponent(comp2, "Test2");
        _canvas.AddComponent(comp3, "Test3");

        _lockViewModel.RefreshCommands();
        _lockViewModel.LockedComponentCount.ShouldBe(3);

        // Act
        _lockViewModel.UnlockAllComponentsCommand.Execute(null);

        // Assert
        comp1.IsLocked.ShouldBeFalse();
        comp2.IsLocked.ShouldBeFalse();
        comp3.IsLocked.ShouldBeFalse();
        _lockViewModel.LockedComponentCount.ShouldBe(0);
    }

    [Fact]
    public void UnlockAllConnections_UnlocksAllConnectionsInCanvas()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 500;
        comp2.PhysicalY = 0;

        _canvas.AddComponent(comp1, "Test1");
        _canvas.AddComponent(comp2, "Test2");

        var connection = TestComponentFactory.CreateConnection(comp1, comp2);
        connection.IsLocked = true;

        _canvas.ConnectionManager.AddExistingConnection(connection);
        _canvas.Connections.Add(new WaveguideConnectionViewModel(connection));

        _lockViewModel.RefreshCommands();
        _lockViewModel.LockedConnectionCount.ShouldBe(1);

        // Act
        _lockViewModel.UnlockAllConnectionsCommand.Execute(null);

        // Assert
        connection.IsLocked.ShouldBeFalse();
        _lockViewModel.LockedConnectionCount.ShouldBe(0);
    }

    [Fact]
    public void UpdateLockCounts_ReflectsCurrentState()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        var comp3 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 500;
        comp2.PhysicalY = 0;
        comp3.PhysicalX = 1000;
        comp3.PhysicalY = 0;

        _canvas.AddComponent(comp1, "Test1");
        _canvas.AddComponent(comp2, "Test2");
        _canvas.AddComponent(comp3, "Test3");

        // Initially unlocked
        _lockViewModel.RefreshCommands();
        _lockViewModel.LockedComponentCount.ShouldBe(0);

        // Lock two components
        comp1.IsLocked = true;
        comp2.IsLocked = true;

        // Act
        _lockViewModel.RefreshCommands();

        // Assert
        _lockViewModel.LockedComponentCount.ShouldBe(2);
    }

    [Fact]
    public void LockSelectedComponentsCommand_IsDisabled_WhenNoUnlockedComponentsSelected()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp1.IsLocked = true;

        var vm1 = _canvas.AddComponent(comp1, "Test1");
        _canvas.Selection.AddToSelection(vm1);

        // Act
        _lockViewModel.RefreshCommands();

        // Assert
        _lockViewModel.LockSelectedComponentsCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void UnlockSelectedComponentsCommand_IsDisabled_WhenNoLockedComponentsSelected()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp1.IsLocked = false;

        var vm1 = _canvas.AddComponent(comp1, "Test1");
        _canvas.Selection.AddToSelection(vm1);

        // Act
        _lockViewModel.RefreshCommands();

        // Assert
        _lockViewModel.UnlockSelectedComponentsCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void StatusText_UpdatesAfterLockOperation()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;

        var vm1 = _canvas.AddComponent(comp1, "Test1");
        _canvas.Selection.AddToSelection(vm1);

        // Act
        _lockViewModel.LockSelectedComponentsCommand.Execute(null);

        // Assert
        _lockViewModel.StatusText.ShouldNotBeNullOrEmpty();
        _lockViewModel.StatusText.ShouldContain("Locked");
    }
}
