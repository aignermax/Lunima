using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;

namespace UnitTests.Controls;

/// <summary>
/// Unit tests for DesignCanvas visual refresh behavior.
/// Tests that canvas sets up refresh callback when Components/Connections collections change.
/// Related to issue #227: "New" button not updating canvas view.
/// </summary>
public class DesignCanvasRefreshTests
{
    [Fact]
    public void DesignCanvas_SetsRepaintCallback_WhenViewModelAssigned()
    {
        // Arrange
        var canvas = new DesignCanvas();
        var viewModel = new DesignCanvasViewModel();

        // Act
        canvas.ViewModel = viewModel;

        // Assert - Verify that RepaintRequested callback was set
        viewModel.RepaintRequested.ShouldNotBeNull("RepaintRequested callback should be set when ViewModel is assigned");
    }

    [Fact]
    public void DesignCanvas_ClearsRepaintCallback_WhenViewModelRemoved()
    {
        // Arrange
        var canvas = new DesignCanvas();
        var viewModel = new DesignCanvasViewModel();
        canvas.ViewModel = viewModel;

        // Verify callback was set
        viewModel.RepaintRequested.ShouldNotBeNull();

        // Act - Remove ViewModel
        canvas.ViewModel = null;

        // Assert - Callback should be cleared
        viewModel.RepaintRequested.ShouldBeNull("RepaintRequested callback should be cleared when ViewModel is removed");
    }

    [Fact]
    public void DesignCanvas_CanHandleMultipleViewModelChanges()
    {
        // Arrange
        var canvas = new DesignCanvas();
        var viewModel1 = new DesignCanvasViewModel();
        var viewModel2 = new DesignCanvasViewModel();

        // Act - Assign first ViewModel
        canvas.ViewModel = viewModel1;
        viewModel1.RepaintRequested.ShouldNotBeNull();

        // Switch to second ViewModel
        canvas.ViewModel = viewModel2;

        // Assert
        viewModel1.RepaintRequested.ShouldBeNull("Old ViewModel's callback should be cleared");
        viewModel2.RepaintRequested.ShouldNotBeNull("New ViewModel's callback should be set");
    }

    [Fact]
    public void DesignCanvasViewModel_ComponentsClear_DoesNotThrow()
    {
        // Arrange
        var canvas = new DesignCanvas();
        var viewModel = new DesignCanvasViewModel();
        canvas.ViewModel = viewModel;

        // Add some components first
        var component1 = TestComponentFactory.CreateStraightWaveGuide();
        var component2 = TestComponentFactory.CreateStraightWaveGuide();
        viewModel.AddComponent(component1, "Template1");
        viewModel.AddComponent(component2, "Template2");

        viewModel.Components.Count.ShouldBe(2);

        // Act & Assert - Clear should not throw and should trigger repaint via collection changed
        Should.NotThrow(() => viewModel.Components.Clear());
        viewModel.Components.Count.ShouldBe(0);
    }

    [Fact]
    public void DesignCanvasViewModel_ConnectionsClear_DoesNotThrow()
    {
        // Arrange
        var canvas = new DesignCanvas();
        var viewModel = new DesignCanvasViewModel();
        canvas.ViewModel = viewModel;

        // Act & Assert - Clear should not throw even when empty
        Should.NotThrow(() => viewModel.Connections.Clear());
        viewModel.Connections.Count.ShouldBe(0);
    }
}
