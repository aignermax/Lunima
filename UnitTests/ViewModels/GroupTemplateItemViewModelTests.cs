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

    [Fact(Skip = "Requires file system integration - GroupLibraryManager.RemoveTemplate needs saved files")]
    public void DeleteCommand_CallsParentRemoveTemplate()
    {
        // This test is skipped because:
        // 1. GroupLibraryManager.RemoveTemplate requires templates to have FilePath (saved to disk)
        // 2. There's no AddTemplate method - only SaveTemplate which writes to disk
        // 3. This is actually an integration test, not a unit test
        //
        // TODO: Either convert to integration test with temp files, or mock GroupLibraryManager

        // Arrange
        var template = new GroupTemplate
        {
            Name = "TestGroup",
            Source = "User"
        };
        var libraryManager = new GroupLibraryManager();
        var library = new ComponentLibraryViewModel(libraryManager);

        // Add template to library UI (ObservableCollection)
        library.AddTemplate(template);
        var countAfterAdd = library.UserGroups.Count;
        countAfterAdd.ShouldBeGreaterThan(0, "Should have at least one template after adding");

        var itemVm = new GroupTemplateItemViewModel(template, library);

        // Act
        itemVm.DeleteCommand.Execute(null);

        // Assert
        library.UserGroups.Count.ShouldBe(countAfterAdd - 1, "Template should be removed from library after delete");
    }
}
