using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.Services;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Tests for keyboard shortcuts functionality, specifically Ctrl+G (Group) and Ctrl+Shift+G (Ungroup).
/// These tests verify that the keyboard shortcuts properly invoke the underlying commands with correct preconditions.
/// </summary>
public class KeyboardShortcutsTests
{
    [Fact]
    public void CreateGroupCommand_WithTwoComponentsSelected_ExecutesSuccessfully()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var interaction = new CanvasInteractionViewModel(canvas, commandManager);

        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);

        // Act - Simulate Ctrl+G keyboard shortcut
        interaction.CreateGroupCommand.Execute(null);

        // Assert - Group created
        canvas.Components.Count.ShouldBe(1);
        canvas.Components[0].Component.ShouldBeOfType<ComponentGroup>();
    }

    [Fact]
    public void CreateGroupCommand_WithOneComponentSelected_CannotExecute()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var interaction = new CanvasInteractionViewModel(canvas, commandManager);

        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var vm1 = canvas.AddComponent(comp1);

        canvas.Selection.AddToSelection(vm1);

        // Act & Assert - Ctrl+G should not execute with only 1 component
        interaction.CreateGroupCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void CreateGroupCommand_WithNoSelection_CannotExecute()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var interaction = new CanvasInteractionViewModel(canvas, commandManager);

        // Act & Assert - Ctrl+G should not execute with no selection
        interaction.CreateGroupCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void UngroupCommand_WithGroupSelected_ExecutesSuccessfully()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var interaction = new CanvasInteractionViewModel(canvas, commandManager);

        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);

        // Create group first
        interaction.CreateGroupCommand.Execute(null);

        canvas.Components.Count.ShouldBe(1);
        var groupVm = canvas.Components[0];
        groupVm.Component.ShouldBeOfType<ComponentGroup>();

        // Select the group
        canvas.Selection.ClearSelection();
        canvas.Selection.AddToSelection(groupVm);

        // Act - Simulate Ctrl+Shift+G keyboard shortcut
        interaction.UngroupCommand.Execute(null);

        // Assert - Components restored
        canvas.Components.Count.ShouldBe(2);
        canvas.Components.ShouldNotContain(c => c.Component is ComponentGroup);
    }

    [Fact]
    public void UngroupCommand_WithNonGroupSelected_CannotExecute()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var interaction = new CanvasInteractionViewModel(canvas, commandManager);

        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var vm1 = canvas.AddComponent(comp1);

        canvas.Selection.AddToSelection(vm1);

        // Act & Assert - Ctrl+Shift+G should not execute with non-group selected
        interaction.UngroupCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void UngroupCommand_WithNoSelection_CannotExecute()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var interaction = new CanvasInteractionViewModel(canvas, commandManager);

        // Act & Assert - Ctrl+Shift+G should not execute with no selection
        interaction.UngroupCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void UngroupCommand_WithMultipleComponentsIncludingGroup_CannotExecute()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var interaction = new CanvasInteractionViewModel(canvas, commandManager);

        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);
        var comp3 = CreateTestComponent("Comp3", 300, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);
        var vm3 = canvas.AddComponent(comp3);

        // Create a group with comp1 and comp2
        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);
        interaction.CreateGroupCommand.Execute(null);

        var groupVm = canvas.Components.First(c => c.Component is ComponentGroup);

        // Select both the group and comp3
        canvas.Selection.ClearSelection();
        canvas.Selection.AddToSelection(groupVm);
        canvas.Selection.AddToSelection(vm3);

        // Act & Assert - Ctrl+Shift+G should not execute with multiple selection
        interaction.UngroupCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void GroupUngroup_UsingCommandManager_SupportsUndoRedo()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var interaction = new CanvasInteractionViewModel(canvas, commandManager);

        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);

        // Act - Group via keyboard shortcut
        interaction.CreateGroupCommand.Execute(null);
        canvas.Components.Count.ShouldBe(1);

        // Undo
        commandManager.Undo().ShouldBeTrue();
        canvas.Components.Count.ShouldBe(2);

        // Redo
        commandManager.Redo().ShouldBeTrue();
        canvas.Components.Count.ShouldBe(1);

        // Select the group and ungroup
        var groupVm = canvas.Components[0];
        canvas.Selection.ClearSelection();
        canvas.Selection.AddToSelection(groupVm);

        interaction.UngroupCommand.Execute(null);
        canvas.Components.Count.ShouldBe(2);

        // Undo ungroup
        commandManager.Undo().ShouldBeTrue();
        canvas.Components.Count.ShouldBe(1);
        canvas.Components[0].Component.ShouldBeOfType<ComponentGroup>();
    }

    [Fact]
    public void CreateGroupCommand_WithThreeComponents_CreatesGroupCorrectly()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var interaction = new CanvasInteractionViewModel(canvas, commandManager);

        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);
        var comp3 = CreateTestComponent("Comp3", 300, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);
        var vm3 = canvas.AddComponent(comp3);

        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);
        canvas.Selection.AddToSelection(vm3);

        // Act - Ctrl+G with 3 components
        interaction.CreateGroupCommand.Execute(null);

        // Assert
        canvas.Components.Count.ShouldBe(1);
        var group = (ComponentGroup)canvas.Components[0].Component;
        group.ChildComponents.Count.ShouldBe(3);
    }

    /// <summary>
    /// Helper to create a simple test component.
    /// </summary>
    private Component CreateTestComponent(string identifier, double x, double y)
    {
        var sMatrix = new SMatrix(new List<Guid>(), new List<(Guid sliderID, double value)>());
        var component = new Component(
            new Dictionary<int, SMatrix> { { 1550, sMatrix } },
            new List<Slider>(),
            "test",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            identifier,
            new DiscreteRotation(),
            new List<PhysicalPin>()
        )
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = 50,
            HeightMicrometers = 30
        };
        return component;
    }
}
