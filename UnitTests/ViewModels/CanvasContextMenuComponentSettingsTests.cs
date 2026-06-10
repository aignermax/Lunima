using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Tests that verify the "Component Settings…" canvas context menu entry
/// is correctly wired through <see cref="CanvasInteractionViewModel"/>.
/// </summary>
public class CanvasContextMenuComponentSettingsTests
{
    [Fact]
    public void OpenSelectedComponentSettingsCommand_IsDisabled_WhenNoComponentSelected()
    {
        var (interaction, _) = CreateInteraction();

        interaction.SelectedComponent = null;

        interaction.OpenSelectedComponentSettingsCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void OpenSelectedComponentSettingsCommand_IsEnabled_WhenComponentSelected()
    {
        var (interaction, _) = CreateInteraction();
        var compVm = TestComponentFactory.CreateComponentViewModel();

        interaction.SelectedComponent = compVm;

        interaction.OpenSelectedComponentSettingsCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void OpenSelectedComponentSettingsCommand_InvokesCallback_WithSelectedComponent()
    {
        var (interaction, _) = CreateInteraction();
        var compVm = TestComponentFactory.CreateComponentViewModel();
        ComponentViewModel? received = null;
        interaction.OpenComponentSettings = vm => received = vm;

        interaction.SelectedComponent = compVm;
        interaction.OpenSelectedComponentSettingsCommand.Execute(null);

        received.ShouldBe(compVm);
    }

    [Fact]
    public void OpenSelectedComponentSettingsCommand_DoesNotInvokeCallback_WhenNoComponentSelected()
    {
        var (interaction, _) = CreateInteraction();
        var invoked = false;
        interaction.OpenComponentSettings = _ => invoked = true;

        interaction.SelectedComponent = null;
        // Command should not execute (CanExecute = false), but guard anyway:
        if (interaction.OpenSelectedComponentSettingsCommand.CanExecute(null))
            interaction.OpenSelectedComponentSettingsCommand.Execute(null);

        invoked.ShouldBeFalse();
    }

    [Fact]
    public void OpenSelectedComponentSettingsCommand_WorksWithoutCallbackWired_DoesNotThrow()
    {
        var (interaction, _) = CreateInteraction();
        var compVm = TestComponentFactory.CreateComponentViewModel();
        // No callback assigned — should not throw
        interaction.SelectedComponent = compVm;

        Should.NotThrow(() => interaction.OpenSelectedComponentSettingsCommand.Execute(null));
    }

    [Fact]
    public void SelectComponentAt_SelectsComponentUnderCursor_AndSyncsSelectionSet()
    {
        var (interaction, canvas) = CreateInteraction();
        var comp = AddComponentAt(canvas, x: 100, y: 100);

        interaction.SelectComponentAt(110, 110);

        interaction.SelectedComponent.ShouldBe(comp);
        canvas.Selection.SelectedComponents.ShouldContain(comp);
        interaction.OpenSelectedComponentSettingsCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void SelectComponentAt_SwitchesToRightClickedComponent_NotPreviousSelection()
    {
        var (interaction, canvas) = CreateInteraction();
        var a = AddComponentAt(canvas, x: 0, y: 0);
        var b = AddComponentAt(canvas, x: 200, y: 200);

        interaction.SelectComponentAt(10, 10);   // select A first
        interaction.SelectComponentAt(210, 210); // right-click B while A is selected

        interaction.SelectedComponent.ShouldBe(b);
        canvas.Selection.SelectedComponents.ShouldHaveSingleItem().ShouldBe(b);
    }

    [Fact]
    public void SelectComponentAt_EmptySpace_ClearsSelection()
    {
        var (interaction, canvas) = CreateInteraction();
        var comp = AddComponentAt(canvas, x: 0, y: 0);
        interaction.SelectComponentAt(10, 10);

        interaction.SelectComponentAt(5000, 5000); // empty canvas region

        interaction.SelectedComponent.ShouldBeNull();
        canvas.Selection.SelectedComponents.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------

    private static (CanvasInteractionViewModel interaction, DesignCanvasViewModel canvas) CreateInteraction()
    {
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        return (new CanvasInteractionViewModel(canvas, commandManager), canvas);
    }

    private static ComponentViewModel AddComponentAt(
        DesignCanvasViewModel canvas, double x, double y, double width = 50, double height = 50)
    {
        var component = TestComponentFactory.CreateStraightWaveGuide();
        component.PhysicalX = x;
        component.PhysicalY = y;
        component.WidthMicrometers = width;
        component.HeightMicrometers = height;
        return canvas.AddComponent(component);
    }
}
