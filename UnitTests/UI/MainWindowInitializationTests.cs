using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CAP.Avalonia;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// UI integration tests that verify MainWindow initializes correctly with proper DI setup.
/// These tests run in headless mode (no GUI required) and work in CI/GitHub Actions/WSL.
/// </summary>
public class MainWindowInitializationTests
{
    /// <summary>
    /// Verifies that MainWindow can be created and initialized with a valid MainViewModel.
    /// This catches DI registration issues that would break the entire UI.
    /// </summary>
    [AvaloniaFact]
    public void MainWindow_Initializes_WithValidDataContext()
    {
        // Arrange - Get MainViewModel from DI container (tests DI setup)
        var mainVm = App.Services.GetRequiredService<MainViewModel>();

        // Act - Create and show window (tests initialization)
        var window = new MainWindow { DataContext = mainVm };
        window.Show();

        // Assert - Verify core ViewModels are accessible
        window.DataContext.ShouldNotBeNull();
        mainVm.ShouldNotBeNull();
        mainVm.CanvasInteraction.ShouldNotBeNull();
        mainVm.LeftPanel.ShouldNotBeNull();
        mainVm.RightPanel.ShouldNotBeNull();
    }

    /// <summary>
    /// Verifies that toolbar commands are initialized and accessible.
    /// This would fail if panel extraction breaks command binding.
    /// </summary>
    [AvaloniaFact]
    public void MainWindow_ToolbarCommands_AreAccessible()
    {
        // Arrange
        var mainVm = App.Services.GetRequiredService<MainViewModel>();
        var window = new MainWindow { DataContext = mainVm };
        window.Show();

        // Assert - Verify critical commands exist and are executable
        mainVm.SetSelectModeCommand.ShouldNotBeNull();
        mainVm.SetConnectModeCommand.ShouldNotBeNull();
        mainVm.SetDeleteModeCommand.ShouldNotBeNull();
        mainVm.ZoomInCommand.ShouldNotBeNull();
        mainVm.ZoomOutCommand.ShouldNotBeNull();
        mainVm.LoadPdkCommand.ShouldNotBeNull();
        mainVm.RunSimulationCommand.ShouldNotBeNull();

        // These should be executable on startup
        mainVm.SetSelectModeCommand.CanExecute(null).ShouldBeTrue();
        mainVm.SetConnectModeCommand.CanExecute(null).ShouldBeTrue();
        mainVm.SetDeleteModeCommand.CanExecute(null).ShouldBeTrue();
    }

    /// <summary>
    /// Verifies that DesignCanvasViewModel is properly initialized.
    /// This is critical for component placement functionality.
    /// </summary>
    [AvaloniaFact]
    public void MainWindow_DesignCanvas_IsInitialized()
    {
        // Arrange
        var mainVm = App.Services.GetRequiredService<MainViewModel>();
        var window = new MainWindow { DataContext = mainVm };
        window.Show();

        // Assert - Verify canvas ViewModel is accessible
        var canvasVm = mainVm.CanvasInteraction;
        canvasVm.ShouldNotBeNull();
        mainVm.Canvas.Components.ShouldNotBeNull();
        canvasVm.CurrentMode.ShouldBe(InteractionMode.Select);
    }

    /// <summary>
    /// Verifies that the component library panel is properly initialized.
    /// This ensures PDK loading functionality is available.
    /// </summary>
    [AvaloniaFact]
    public void MainWindow_ComponentLibrary_IsInitialized()
    {
        // Arrange
        var mainVm = App.Services.GetRequiredService<MainViewModel>();
        var window = new MainWindow { DataContext = mainVm };
        window.Show();

        // Assert - Verify component library ViewModel exists
        mainVm.LeftPanel.ComponentLibrary.ShouldNotBeNull();
        mainVm.LeftPanel.ComponentLibrary.UserGroups.ShouldNotBeNull();
        mainVm.LeftPanel.ComponentLibrary.PdkGroups.ShouldNotBeNull();
    }

    /// <summary>
    /// Verifies that the AI assistant panel is properly initialized (if present).
    /// </summary>
    [AvaloniaFact]
    public void MainWindow_AiAssistant_IsInitialized()
    {
        // Arrange
        var mainVm = App.Services.GetRequiredService<MainViewModel>();
        var window = new MainWindow { DataContext = mainVm };
        window.Show();

        // Assert - Verify AI assistant ViewModel exists
        mainVm.RightPanel.AiAssistant.ShouldNotBeNull();
    }
}
