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
/// Tests for DeleteComponentCommand to ensure undo/redo works correctly.
/// </summary>
public class DeleteComponentCommandTests
{
    [Fact]
    public void DeleteComponent_ThenUndo_RestoresComponentAtOriginalPosition()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var component = CreateTestComponent("TestComp", 100, 200);
        var vm = canvas.AddComponent(component, "TestTemplate");

        // Store original position
        var originalX = vm.X;
        var originalY = vm.Y;
        originalX.ShouldBe(100);
        originalY.ShouldBe(200);

        // Act - Delete the component
        var deleteCmd = new DeleteComponentCommand(canvas, vm);
        deleteCmd.Execute();

        // Assert - Component should be removed
        canvas.Components.Count.ShouldBe(0);

        // Act - Undo the delete
        deleteCmd.Undo();

        // Assert - Component should be restored at original position
        canvas.Components.Count.ShouldBe(1);
        var restoredVm = canvas.Components[0];
        restoredVm.X.ShouldBe(originalX, "Component X position should be restored");
        restoredVm.Y.ShouldBe(originalY, "Component Y position should be restored");
        restoredVm.Name.ShouldBe("TestComp");
    }

    [Fact]
    public void DeleteComponent_AfterMove_UndoRestoresAtDeleteTimePosition()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var component = CreateTestComponent("TestComp", 100, 200);
        var vm = canvas.AddComponent(component, "TestTemplate");

        // Move the component
        canvas.MoveComponent(vm, 50, 30); // Move to (150, 230)
        vm.X.ShouldBe(150);
        vm.Y.ShouldBe(230);

        // Store position at delete time
        var positionAtDeleteTime = (vm.X, vm.Y);

        // Act - Delete the component
        var deleteCmd = new DeleteComponentCommand(canvas, vm);
        deleteCmd.Execute();
        canvas.Components.Count.ShouldBe(0);

        // Act - Undo the delete
        deleteCmd.Undo();

        // Assert - Component should be restored at the position it had when deleted (150, 230)
        canvas.Components.Count.ShouldBe(1);
        var restoredVm = canvas.Components[0];
        restoredVm.X.ShouldBe(positionAtDeleteTime.X, "Component should be at position when deleted");
        restoredVm.Y.ShouldBe(positionAtDeleteTime.Y, "Component should be at position when deleted");
    }

    [Fact]
    public void DeleteComponent_UndoRedo_MaintainsCorrectPosition()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var component = CreateTestComponent("TestComp", 100, 200);
        var vm = canvas.AddComponent(component, "TestTemplate");

        // Move component before delete
        canvas.MoveComponent(vm, 50, 30); // Now at (150, 230)
        var deleteTimePosition = (vm.X, vm.Y);

        var deleteCmd = new DeleteComponentCommand(canvas, vm);

        // Act - Delete, Undo, Redo, Undo cycle
        deleteCmd.Execute();
        canvas.Components.Count.ShouldBe(0);

        deleteCmd.Undo();
        canvas.Components.Count.ShouldBe(1);
        var vm1 = canvas.Components[0];
        vm1.X.ShouldBe(deleteTimePosition.X);
        vm1.Y.ShouldBe(deleteTimePosition.Y);

        deleteCmd.Execute(); // Redo the delete
        canvas.Components.Count.ShouldBe(0);

        deleteCmd.Undo(); // Undo again
        canvas.Components.Count.ShouldBe(1);
        var vm2 = canvas.Components[0];
        vm2.X.ShouldBe(deleteTimePosition.X, "Position should remain stable across multiple undo/redo");
        vm2.Y.ShouldBe(deleteTimePosition.Y, "Position should remain stable across multiple undo/redo");
    }

    [Fact]
    public void DeleteComponent_WithPins_UndoRestoresPinPositions()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var component = CreateTestComponentWithPins("TestComp", 100, 200);
        var vm = canvas.AddComponent(component, "TestTemplate");

        // Get pin absolute positions before delete
        var pin = component.PhysicalPins[0];
        var (pinXBefore, pinYBefore) = pin.GetAbsolutePosition();
        pinXBefore.ShouldBe(110); // 100 + 10 offset
        pinYBefore.ShouldBe(225); // 200 + 25 offset

        // Delete
        var deleteCmd = new DeleteComponentCommand(canvas, vm);
        deleteCmd.Execute();

        // Undo
        deleteCmd.Undo();

        // Assert - Pins should be at correct absolute positions
        var restoredComp = canvas.Components[0].Component;
        var restoredPin = restoredComp.PhysicalPins[0];
        var (pinXAfter, pinYAfter) = restoredPin.GetAbsolutePosition();
        pinXAfter.ShouldBe(pinXBefore, "Pin X position should be restored");
        pinYAfter.ShouldBe(pinYBefore, "Pin Y position should be restored");
    }

    [Fact]
    public void DeleteAfterMove_UndoTwice_ComponentMovesBackCorrectly()
    {
        // This test reproduces the bug where:
        // 1. Move component from (100, 100) to (200, 200)
        // 2. Delete component
        // 3. Undo delete (component reappears at 200, 200)
        // 4. Undo move (component should move back to 100, 100)
        // Bug: Component box doesn't move, only pins move

        // Arrange
        var canvas = new DesignCanvasViewModel();
        var component = CreateTestComponentWithPins("TestComp", 100, 100);
        var vm = canvas.AddComponent(component, "TestTemplate");

        // Store original position and pin position
        var originalX = vm.X;
        var originalY = vm.Y;
        var pin = component.PhysicalPins[0];
        var (originalPinX, originalPinY) = pin.GetAbsolutePosition();

        // Act 1 - Move the component
        var moveCmd = new MoveComponentCommand(canvas, vm, originalX, originalY, 200, 200);
        moveCmd.Execute();
        vm = canvas.Components[0]; // Get updated reference
        vm.X.ShouldBe(200);
        vm.Y.ShouldBe(200);
        var (movedPinX, movedPinY) = pin.GetAbsolutePosition();
        movedPinX.ShouldBe(originalPinX + 100); // Pin moved with component
        movedPinY.ShouldBe(originalPinY + 100);

        // Act 2 - Delete the component
        var deleteCmd = new DeleteComponentCommand(canvas, vm);
        deleteCmd.Execute();
        canvas.Components.Count.ShouldBe(0);

        // Act 3 - Undo delete (component reappears at deleted position)
        deleteCmd.Undo();
        canvas.Components.Count.ShouldBe(1);
        vm = canvas.Components[0]; // Get NEW ComponentViewModel created by undo
        vm.X.ShouldBe(200, "Component should be at position when deleted");
        vm.Y.ShouldBe(200);

        // Act 4 - Undo move (this is where the bug occurs)
        moveCmd.Undo();

        // Assert - BOTH component box AND pins should be at original position
        vm = canvas.Components[0];
        vm.X.ShouldBe(originalX, "Component box should move back to original position");
        vm.Y.ShouldBe(originalY, "Component box should move back to original position");

        var (finalPinX, finalPinY) = pin.GetAbsolutePosition();
        finalPinX.ShouldBe(originalPinX, "Pin should be at original position");
        finalPinY.ShouldBe(originalPinY, "Pin should be at original position");
    }

    private static Component CreateTestComponent(string identifier, double x, double y)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "test_function",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: identifier,
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: new List<PhysicalPin>()
        );

        component.WidthMicrometers = 50;
        component.HeightMicrometers = 30;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }

    private static Component CreateTestComponentWithPins(string identifier, double x, double y)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var pins = new List<PhysicalPin>
        {
            new PhysicalPin { Name = "in", OffsetXMicrometers = 10, OffsetYMicrometers = 25, AngleDegrees = 180 }
        };

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "test_function",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: identifier,
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: pins
        );

        component.WidthMicrometers = 50;
        component.HeightMicrometers = 30;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }
}
