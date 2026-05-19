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

    // -------------------------------------------------------------------------

    private static (CanvasInteractionViewModel interaction, DesignCanvasViewModel canvas) CreateInteraction()
    {
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        return (new CanvasInteractionViewModel(canvas, commandManager), canvas);
    }
}
