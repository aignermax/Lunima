using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Tests that commands correctly respect the IsLocked state of components and connections.
/// </summary>
public class LockedComponentCommandTests
{
    [Fact]
    public void MoveComponentCommand_LockedComponent_DoesNotMove()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp = TestComponentFactory.CreateBasicComponent();
        comp.PhysicalX = 100;
        comp.PhysicalY = 100;
        comp.IsLocked = true;

        var vm = canvas.AddComponent(comp, "TestComponent");
        double startX = vm.X;
        double startY = vm.Y;

        var cmd = new MoveComponentCommand(canvas, vm, startX, startY, startX + 50, startY + 50);

        // Act
        cmd.Execute();

        // Assert - component should not have moved
        vm.X.ShouldBe(startX);
        vm.Y.ShouldBe(startY);
    }

    [Fact]
    public void MoveComponentCommand_UnlockedComponent_Moves()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp = TestComponentFactory.CreateBasicComponent();
        comp.PhysicalX = 100;
        comp.PhysicalY = 100;
        comp.IsLocked = false;

        var vm = canvas.AddComponent(comp, "TestComponent");
        double startX = vm.X;
        double startY = vm.Y;
        double endX = startX + 50;
        double endY = startY + 50;

        var cmd = new MoveComponentCommand(canvas, vm, startX, startY, endX, endY);

        // Act
        cmd.Execute();

        // Assert - component should have moved
        vm.X.ShouldBe(endX);
        vm.Y.ShouldBe(endY);
    }

    [Fact]
    public void DeleteComponentCommand_LockedComponent_DoesNotDelete()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp = TestComponentFactory.CreateBasicComponent();
        comp.PhysicalX = 100;
        comp.PhysicalY = 100;
        comp.IsLocked = true;

        var vm = canvas.AddComponent(comp, "TestComponent");
        int initialCount = canvas.Components.Count;

        var cmd = new DeleteComponentCommand(canvas, vm);

        // Act
        cmd.Execute();

        // Assert - component should not have been deleted
        canvas.Components.Count.ShouldBe(initialCount);
        canvas.Components.ShouldContain(vm);
    }

    [Fact]
    public void DeleteComponentCommand_UnlockedComponent_Deletes()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp = TestComponentFactory.CreateBasicComponent();
        comp.PhysicalX = 100;
        comp.PhysicalY = 100;
        comp.IsLocked = false;

        var vm = canvas.AddComponent(comp, "TestComponent");
        int initialCount = canvas.Components.Count;

        var cmd = new DeleteComponentCommand(canvas, vm);

        // Act
        cmd.Execute();

        // Assert - component should have been deleted
        canvas.Components.Count.ShouldBe(initialCount - 1);
        canvas.Components.ShouldNotContain(vm);
    }

    [Fact]
    public void RotateComponentCommand_LockedComponent_DoesNotRotate()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp = TestComponentFactory.CreateBasicComponent();
        comp.PhysicalX = 100;
        comp.PhysicalY = 100;
        comp.IsLocked = true;

        var vm = canvas.AddComponent(comp, "TestComponent");
        var initialRotation = comp.RotationDegrees;

        var cmd = new RotateComponentCommand(canvas, vm);

        // Act
        cmd.Execute();

        // Assert - component should not have rotated
        comp.RotationDegrees.ShouldBe(initialRotation);
        cmd.WasApplied.ShouldBeFalse();
    }

    [Fact]
    public void RotateComponentCommand_UnlockedComponent_Rotates()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp = TestComponentFactory.CreateBasicComponent();
        comp.PhysicalX = 100;
        comp.PhysicalY = 100;
        comp.IsLocked = false;

        var vm = canvas.AddComponent(comp, "TestComponent");
        var initialRotation = comp.RotationDegrees;

        var cmd = new RotateComponentCommand(canvas, vm);

        // Act
        cmd.Execute();

        // Assert - component should have rotated 90 degrees
        comp.RotationDegrees.ShouldBe((initialRotation + 90) % 360);
        cmd.WasApplied.ShouldBeTrue();
    }

    [Fact]
    public void DeleteConnectionCommand_LockedConnection_DoesNotDelete()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 500;
        comp2.PhysicalY = 0;

        canvas.AddComponent(comp1, "Test1");
        canvas.AddComponent(comp2, "Test2");

        var connection = TestComponentFactory.CreateConnection(comp1, comp2);
        connection.IsLocked = true;
        canvas.ConnectionManager.AddExistingConnection(connection);
        var connVm = new WaveguideConnectionViewModel(connection);
        canvas.Connections.Add(connVm);

        int initialCount = canvas.Connections.Count;

        var cmd = new DeleteConnectionCommand(canvas, connVm);

        // Act
        cmd.Execute();

        // Assert - connection should not have been deleted
        canvas.Connections.Count.ShouldBe(initialCount);
        canvas.Connections.ShouldContain(connVm);
    }

    [Fact]
    public void DeleteConnectionCommand_UnlockedConnection_Deletes()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 500;
        comp2.PhysicalY = 0;

        canvas.AddComponent(comp1, "Test1");
        canvas.AddComponent(comp2, "Test2");

        var connection = TestComponentFactory.CreateConnection(comp1, comp2);
        connection.IsLocked = false;
        canvas.ConnectionManager.AddExistingConnection(connection);
        var connVm = new WaveguideConnectionViewModel(connection);
        canvas.Connections.Add(connVm);

        int initialCount = canvas.Connections.Count;

        var cmd = new DeleteConnectionCommand(canvas, connVm);

        // Act
        cmd.Execute();

        // Assert - connection should have been deleted
        canvas.Connections.Count.ShouldBe(initialCount - 1);
        canvas.Connections.ShouldNotContain(connVm);
    }
}
