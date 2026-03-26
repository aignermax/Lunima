using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Creation;
using Shouldly;
using Xunit;
using UnitTests.Helpers;

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
        // This is an integration test that uses the file system
        // We need to create a real saved template to test deletion

        // Arrange - Create temp directory for test
        var tempDir = Path.Combine(Path.GetTempPath(), $"GroupLibraryTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var libraryManager = new GroupLibraryManager(tempDir);

            // Create a simple test group with children
            var testGroup = TestComponentFactory.CreateComponentGroup("TestGroup", addChildren: true);

            // Save template (this creates the file)
            var savedTemplate = libraryManager.SaveTemplate(
                testGroup,
                "TestGroup",
                "Test description",
                "User"
            );

            savedTemplate.ShouldNotBeNull("SaveTemplate should return a template");
            savedTemplate.FilePath.ShouldNotBeNullOrEmpty("Saved template should have a FilePath");

            // Create ComponentLibraryViewModel and load the saved template
            var library = new ComponentLibraryViewModel(libraryManager);
            library.LoadGroupsCommand.Execute(null);

            var countBefore = library.UserGroups.Count;
            countBefore.ShouldBeGreaterThan(0, "Should have at least one template after loading");

            // Find the template item in the UI
            var templateItem = library.UserGroups.FirstOrDefault(t => t.Template.Name == "TestGroup");
            templateItem.ShouldNotBeNull("Template should be loaded into UI");

            // Act - Execute delete command
            templateItem.DeleteCommand.Execute(null);

            // Assert - Template should be removed from UI and disk
            library.UserGroups.Count.ShouldBe(countBefore - 1, "Template should be removed from library after delete");
            File.Exists(savedTemplate.FilePath).ShouldBeFalse("Template file should be deleted from disk");
        }
        finally
        {
            // Cleanup - Delete temp directory
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
