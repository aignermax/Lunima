using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Tests for auto-switching from Connect mode to Select mode when clicking outside pins.
/// Issue #378: Reduces user confusion when trying to drag components while in Connect mode.
/// </summary>
public class ConnectModeAutoSwitchTests
{
    private static CanvasInteractionViewModel CreateInteraction(DesignCanvasViewModel? canvas = null)
    {
        canvas ??= new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        return new CanvasInteractionViewModel(canvas, commandManager);
    }

    [Fact]
    public void CanvasClicked_InConnectMode_WithNoPinAtPosition_SwitchesToSelectMode()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var interaction = CreateInteraction(canvas);
        interaction.CurrentMode = InteractionMode.Connect;

        // Act: Click on empty canvas area (far from any pin)
        interaction.CanvasClicked(9999.0, 9999.0);

        // Assert
        interaction.CurrentMode.ShouldBe(InteractionMode.Select);
    }

    [Fact]
    public void CanvasClicked_InConnectMode_OnComponentBody_SwitchesToSelectMode()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var interaction = CreateInteraction(canvas);

        // Place a component on the canvas with no pins at center
        var component = TestComponentFactory.CreateBasicComponent();
        component.PhysicalX = 500;
        component.PhysicalY = 500;
        component.WidthMicrometers = 250;
        component.HeightMicrometers = 250;
        component.PhysicalPins.Clear(); // No pins on this component
        canvas.AddComponent(component, "TestTemplate");

        interaction.CurrentMode = InteractionMode.Connect;

        // Act: Click on the component body center (not on a pin)
        interaction.CanvasClicked(625.0, 625.0);

        // Assert: Mode switches to Select because no pin was at click position
        interaction.CurrentMode.ShouldBe(InteractionMode.Select);
    }

    [Fact]
    public void CanvasClicked_InConnectMode_WithPinNearby_DoesNotSwitchToSelectMode()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var interaction = CreateInteraction(canvas);

        // Place a component with a physical pin
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        component.PhysicalX = 100;
        component.PhysicalY = 100;
        component.WidthMicrometers = 250;
        component.HeightMicrometers = 250;
        canvas.AddComponent(component, "WaveguideTemplate");

        interaction.CurrentMode = InteractionMode.Connect;

        // Act: Click exactly on the pin position (absolute = component pos + offset)
        var pin = component.PhysicalPins[0];
        var (pinX, pinY) = pin.GetAbsolutePosition();
        interaction.CanvasClicked(pinX, pinY);

        // Assert: Mode stays in Connect mode (pin click starts a connection)
        interaction.CurrentMode.ShouldBe(InteractionMode.Connect);
    }

    [Fact]
    public void ConnectMode_ClickingOutsidePins_SwitchesToSelectMode()
    {
        // Arrange: User is in Connect mode (from issue test scenario)
        var canvas = new DesignCanvasViewModel();
        var interaction = CreateInteraction(canvas);
        interaction.CurrentMode = InteractionMode.Connect;

        // Act: User clicks on empty canvas area (simulating attempt to drag a component)
        interaction.CanvasClicked(100.0, 100.0);

        // Assert: Mode switched back to Select
        interaction.CurrentMode.ShouldBe(InteractionMode.Select);
    }

    [Fact]
    public void CanvasClicked_InOtherModes_DoesNotAutoSwitchToSelect()
    {
        // Arrange: Delete mode should not auto-switch to Select on empty click
        var canvas = new DesignCanvasViewModel();
        var interaction = CreateInteraction(canvas);
        interaction.CurrentMode = InteractionMode.Delete;

        // Act: Click on empty canvas area
        interaction.CanvasClicked(9999.0, 9999.0);

        // Assert: Delete mode is unchanged (only Connect mode auto-switches)
        interaction.CurrentMode.ShouldBe(InteractionMode.Delete);
    }
}
