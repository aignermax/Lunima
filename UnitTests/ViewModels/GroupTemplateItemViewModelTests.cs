using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Creation;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Tests for GroupTemplateItemViewModel hover state and delete functionality.
/// While we can't fully test UI pointer events, we can verify the ViewModel logic.
/// </summary>
public class GroupTemplateItemViewModelTests
{
    [Fact]
    public void IsHovered_InitiallyFalse()
    {
        // Arrange
        var template = new GroupTemplate
        {
            Name = "TestGroup",
            Source = "User"
        };
        var libraryManager = new GroupLibraryManager();
        var library = new ComponentLibraryViewModel(libraryManager);

        // Act
        var itemVm = new GroupTemplateItemViewModel(template, library);

        // Assert
        itemVm.IsHovered.ShouldBeFalse("IsHovered should initially be false");
    }

    [Fact]
    public void IsHovered_CanBeSetToTrue()
    {
        // Arrange
        var template = new GroupTemplate
        {
            Name = "TestGroup",
            Source = "User"
        };
        var libraryManager = new GroupLibraryManager();
        var library = new ComponentLibraryViewModel(libraryManager);
        var itemVm = new GroupTemplateItemViewModel(template, library);

        // Act
        itemVm.IsHovered = true;

        // Assert
        itemVm.IsHovered.ShouldBeTrue("IsHovered should be true after being set");
    }

    [Fact]
    public void IsHovered_CanBeToggledBackToFalse()
    {
        // Arrange
        var template = new GroupTemplate
        {
            Name = "TestGroup",
            Source = "User"
        };
        var libraryManager = new GroupLibraryManager();
        var library = new ComponentLibraryViewModel(libraryManager);
        var itemVm = new GroupTemplateItemViewModel(template, library);

        // Act
        itemVm.IsHovered = true;
        itemVm.IsHovered.ShouldBeTrue();

        itemVm.IsHovered = false;

        // Assert
        itemVm.IsHovered.ShouldBeFalse("IsHovered should be false after being toggled");
    }

    [Fact]
    public void Template_IsAccessible()
    {
        // Arrange
        var template = new GroupTemplate
        {
            Name = "TestGroup",
            Source = "User",
            ComponentCount = 5
        };
        var libraryManager = new GroupLibraryManager();
        var library = new ComponentLibraryViewModel(libraryManager);

        // Act
        var itemVm = new GroupTemplateItemViewModel(template, library);

        // Assert
        itemVm.Template.ShouldBe(template);
        itemVm.Template.Name.ShouldBe("TestGroup");
        itemVm.Template.ComponentCount.ShouldBe(5);
    }

    [Fact]
    public void DeleteCommand_IsAvailable()
    {
        // Arrange
        var template = new GroupTemplate
        {
            Name = "TestGroup",
            Source = "User"
        };
        var libraryManager = new GroupLibraryManager();
        var library = new ComponentLibraryViewModel(libraryManager);
        var itemVm = new GroupTemplateItemViewModel(template, library);

        // Act & Assert
        itemVm.DeleteCommand.ShouldNotBeNull("DeleteCommand should be available");
        itemVm.DeleteCommand.CanExecute(null).ShouldBeTrue("DeleteCommand should be executable");
    }

    [Fact]
    public void DeleteCommand_CallsParentRemoveTemplate()
    {
        // Arrange
        var template = new GroupTemplate
        {
            Name = "TestGroup",
            Source = "User"
        };
        var libraryManager = new GroupLibraryManager();
        var library = new ComponentLibraryViewModel(libraryManager);

        // Add template to library via ComponentLibraryViewModel
        var countBefore = library.UserGroups.Count;
        library.AddTemplate(template);
        library.UserGroups.Count.ShouldBe(countBefore + 1, "Should have one more template after adding");

        var itemVm = new GroupTemplateItemViewModel(template, library);

        // Act
        itemVm.DeleteCommand.Execute(null);

        // Assert
        library.UserGroups.Count.ShouldBe(countBefore, "Template should be removed from library after delete");
    }
}
