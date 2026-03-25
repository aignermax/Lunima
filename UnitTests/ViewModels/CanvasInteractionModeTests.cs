using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Creation;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Tests for CanvasInteractionViewModel mode switching and template selection/deselection behavior.
/// Verifies that templates are properly deselected when modes change.
/// </summary>
public class CanvasInteractionModeTests
{
    [Fact]
    public void SwitchToSelectMode_DeselectsGroupTemplate()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var libraryManager = new GroupLibraryManager();
        var library = new ComponentLibraryViewModel(libraryManager);
        var interaction = new CanvasInteractionViewModel(canvas, commandManager, library);

        var template = new GroupTemplate
        {
            Name = "TestGroup",
            Source = "User"
        };

        // Act: Select a group template (enters PlaceGroupTemplate mode)
        interaction.SelectedGroupTemplate = template;
        interaction.CurrentMode.ShouldBe(InteractionMode.PlaceGroupTemplate);
        interaction.SelectedGroupTemplate.ShouldBe(template);

        // Switch to Select mode
        interaction.CurrentMode = InteractionMode.Select;

        // Assert: Group template should be deselected
        interaction.SelectedGroupTemplate.ShouldBeNull();
    }

    [Fact]
    public void SwitchToDeleteMode_DeselectsGroupTemplate()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var libraryManager = new GroupLibraryManager();
        var library = new ComponentLibraryViewModel(libraryManager);
        var interaction = new CanvasInteractionViewModel(canvas, commandManager, library);

        var template = new GroupTemplate
        {
            Name = "TestGroup",
            Source = "User"
        };

        // Act: Select a group template
        interaction.SelectedGroupTemplate = template;
        interaction.SelectedGroupTemplate.ShouldBe(template);

        // Switch to Delete mode
        interaction.CurrentMode = InteractionMode.Delete;

        // Assert: Group template should be deselected
        interaction.SelectedGroupTemplate.ShouldBeNull();
    }

    [Fact]
    public void SwitchToConnectMode_DeselectsGroupTemplate()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var libraryManager = new GroupLibraryManager();
        var library = new ComponentLibraryViewModel(libraryManager);
        var interaction = new CanvasInteractionViewModel(canvas, commandManager, library);

        var template = new GroupTemplate
        {
            Name = "TestGroup",
            Source = "User"
        };

        // Act: Select a group template
        interaction.SelectedGroupTemplate = template;
        interaction.SelectedGroupTemplate.ShouldBe(template);

        // Switch to Connect mode
        interaction.CurrentMode = InteractionMode.Connect;

        // Assert: Group template should be deselected
        interaction.SelectedGroupTemplate.ShouldBeNull();
    }

    [Fact]
    public void SelectComponentTemplate_DeselectsGroupTemplate()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var libraryManager = new GroupLibraryManager();
        var library = new ComponentLibraryViewModel(libraryManager);
        var interaction = new CanvasInteractionViewModel(canvas, commandManager, library);

        var groupTemplate = new GroupTemplate
        {
            Name = "TestGroup",
            Source = "User"
        };

        var componentTemplate = new ComponentTemplate
        {
            Name = "TestComponent",
            Category = "Test"
        };

        // Act: Select a group template first
        interaction.SelectedGroupTemplate = groupTemplate;
        interaction.SelectedGroupTemplate.ShouldBe(groupTemplate);

        // Select a component template
        interaction.SelectedTemplate = componentTemplate;

        // Assert: Group template should be deselected
        interaction.SelectedGroupTemplate.ShouldBeNull();
        interaction.SelectedTemplate.ShouldBe(componentTemplate);
    }

    [Fact]
    public void SelectGroupTemplate_DeselectsComponentTemplate()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var libraryManager = new GroupLibraryManager();
        var library = new ComponentLibraryViewModel(libraryManager);
        var interaction = new CanvasInteractionViewModel(canvas, commandManager, library);

        var groupTemplate = new GroupTemplate
        {
            Name = "TestGroup",
            Source = "User"
        };

        var componentTemplate = new ComponentTemplate
        {
            Name = "TestComponent",
            Category = "Test"
        };

        // Act: Select a component template first
        interaction.SelectedTemplate = componentTemplate;
        interaction.SelectedTemplate.ShouldBe(componentTemplate);

        // Select a group template
        interaction.SelectedGroupTemplate = groupTemplate;

        // Assert: Component template should be deselected
        interaction.SelectedTemplate.ShouldBeNull();
        interaction.SelectedGroupTemplate.ShouldBe(groupTemplate);
    }

    [Fact]
    public void ClearLeftPanelGroupSelectionCallback_IsCalled_WhenComponentTemplateSelected()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var libraryManager = new GroupLibraryManager();
        var library = new ComponentLibraryViewModel(libraryManager);
        var interaction = new CanvasInteractionViewModel(canvas, commandManager, library);

        bool callbackCalled = false;
        interaction.ClearLeftPanelGroupSelection = () =>
        {
            callbackCalled = true;
        };

        var componentTemplate = new ComponentTemplate
        {
            Name = "TestComponent",
            Category = "Test"
        };

        // Act: Select a component template
        interaction.SelectedTemplate = componentTemplate;

        // Assert: Callback should be called
        callbackCalled.ShouldBeTrue("ClearLeftPanelGroupSelection callback should be invoked when component template is selected");
    }

    [Fact]
    public void ClearComponentTemplateSelectionCallback_IsCalled_WhenGroupTemplateSelected()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var libraryManager = new GroupLibraryManager();
        var library = new ComponentLibraryViewModel(libraryManager);
        var interaction = new CanvasInteractionViewModel(canvas, commandManager, library);

        bool callbackCalled = false;
        interaction.ClearComponentTemplateSelection = () =>
        {
            callbackCalled = true;
        };

        var groupTemplate = new GroupTemplate
        {
            Name = "TestGroup",
            Source = "User"
        };

        // Act: Select a group template
        interaction.SelectedGroupTemplate = groupTemplate;

        // Assert: Callback should be called
        callbackCalled.ShouldBeTrue("ClearComponentTemplateSelection callback should be invoked when group template is selected");
    }

    [Fact]
    public void ModeChange_CallsBothClearCallbacks()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var libraryManager = new GroupLibraryManager();
        var library = new ComponentLibraryViewModel(libraryManager);
        var interaction = new CanvasInteractionViewModel(canvas, commandManager, library);

        bool groupCallbackCalled = false;
        bool componentCallbackCalled = false;

        interaction.ClearLeftPanelGroupSelection = () =>
        {
            groupCallbackCalled = true;
        };

        interaction.ClearComponentTemplateSelection = () =>
        {
            componentCallbackCalled = true;
        };

        var groupTemplate = new GroupTemplate
        {
            Name = "TestGroup",
            Source = "User"
        };

        // Select a group template to enter PlaceGroupTemplate mode
        interaction.SelectedGroupTemplate = groupTemplate;

        // Reset callback flags
        groupCallbackCalled = false;
        componentCallbackCalled = false;

        // Act: Switch to Select mode
        interaction.CurrentMode = InteractionMode.Select;

        // Assert: Both callbacks should be called
        groupCallbackCalled.ShouldBeTrue("ClearLeftPanelGroupSelection should be called on mode change");
        componentCallbackCalled.ShouldBeTrue("ClearComponentTemplateSelection should be called on mode change");
    }
}
