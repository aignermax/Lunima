using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using Shouldly;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Unit tests for RenameGroupCommand.
/// Tests renaming groups and updating the library.
/// </summary>
public class RenameGroupCommandTests : IDisposable
{
    private readonly string _testLibraryPath;
    private readonly GroupLibraryManager _libraryManager;
    private readonly ComponentLibraryViewModel _libraryViewModel;

    public RenameGroupCommandTests()
    {
        _testLibraryPath = Path.Combine(Path.GetTempPath(), $"RenameGroupTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testLibraryPath);
        _libraryManager = new GroupLibraryManager(_testLibraryPath);
        _libraryViewModel = new ComponentLibraryViewModel(_libraryManager);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testLibraryPath))
        {
            Directory.Delete(_testLibraryPath, true);
        }
    }

    [Fact]
    public void Execute_RenamesGroupAndUpdatesLibrary()
    {
        // Arrange
        var group = CreateTestGroup("Original Name", 2);
        var originalTemplate = _libraryManager.SaveTemplate(group, "Original Name", "Old description", "User");
        _libraryViewModel.AddTemplate(originalTemplate);

        var command = new RenameGroupCommand(
            group,
            _libraryViewModel,
            "New Name",
            "New description");

        // Act
        command.Execute();

        // Assert
        group.GroupName.ShouldBe("New Name");
        group.Description.ShouldBe("New description");
        _libraryViewModel.UserGroups.Count.ShouldBe(1);
        _libraryViewModel.UserGroups.First().Name.ShouldBe("New Name");
    }

    [Fact]
    public void Execute_WithNullDescription_PreservesExistingDescription()
    {
        // Arrange
        var group = CreateTestGroup("Test Group", 1);
        group.Description = "Original description";
        var originalTemplate = _libraryManager.SaveTemplate(group, "Test Group", "Original description", "User");
        _libraryViewModel.AddTemplate(originalTemplate);

        var command = new RenameGroupCommand(
            group,
            _libraryViewModel,
            "Renamed Group",
            null);

        // Act
        command.Execute();

        // Assert
        group.GroupName.ShouldBe("Renamed Group");
        group.Description.ShouldBe("Original description");
    }

    [Fact]
    public void Undo_RestoresOriginalNameAndLibraryEntry()
    {
        // Arrange
        var group = CreateTestGroup("Original", 2);
        group.Description = "Original desc";
        var originalTemplate = _libraryManager.SaveTemplate(group, "Original", "Original desc", "User");
        _libraryViewModel.AddTemplate(originalTemplate);

        var command = new RenameGroupCommand(
            group,
            _libraryViewModel,
            "NewName",
            "NewDesc");

        command.Execute();
        group.GroupName.ShouldBe("NewName");

        // Act
        command.Undo();

        // Assert
        group.GroupName.ShouldBe("Original");
        group.Description.ShouldBe("Original desc");
        _libraryViewModel.UserGroups.Count.ShouldBe(1);
        _libraryViewModel.UserGroups.First().Name.ShouldBe("Original");
    }

    [Fact]
    public void Execute_RemovesOldTemplateFromLibrary()
    {
        // Arrange
        var group = CreateTestGroup("OldName", 1);
        var oldTemplate = _libraryManager.SaveTemplate(group, "OldName");
        _libraryViewModel.AddTemplate(oldTemplate);

        var command = new RenameGroupCommand(group, _libraryViewModel, "NewName");

        // Act
        command.Execute();

        // Assert
        _libraryViewModel.UserGroups.ShouldNotContain(t => t.Name == "OldName");
        _libraryViewModel.UserGroups.ShouldContain(t => t.Name == "NewName");
    }

    [Fact]
    public void Execute_WithEmptyName_DoesNothing()
    {
        // Arrange
        var group = CreateTestGroup("ValidName", 1);
        var command = new RenameGroupCommand(group, _libraryViewModel, "");

        // Act
        command.Execute();

        // Assert - group name unchanged
        group.GroupName.ShouldBe("ValidName");
    }

    [Fact]
    public void Description_ReturnsCorrectMessage()
    {
        // Arrange
        var group = CreateTestGroup("Test", 1);
        var command = new RenameGroupCommand(group, _libraryViewModel, "NewName");

        // Assert
        command.Description.ShouldBe("Rename group to 'NewName'");
    }

    /// <summary>
    /// Creates a test ComponentGroup with the specified number of child components.
    /// </summary>
    private ComponentGroup CreateTestGroup(string name, int childCount)
    {
        var group = new ComponentGroup(name)
        {
            PhysicalX = 0,
            PhysicalY = 0
        };

        for (int i = 0; i < childCount; i++)
        {
            var child = new Component(
                new Dictionary<int, CAP_Core.LightCalculation.SMatrix>(),
                new List<Slider>(),
                "test_component",
                "",
                new Part[1, 1] { { new Part() } },
                -1,
                $"comp_{i}_{Guid.NewGuid():N}",
                DiscreteRotation.R0,
                new List<PhysicalPin>
                {
                    new PhysicalPin
                    {
                        Name = "a0",
                        OffsetXMicrometers = 0,
                        OffsetYMicrometers = 0,
                        AngleDegrees = 180
                    }
                })
            {
                PhysicalX = i * 100,
                PhysicalY = 0,
                WidthMicrometers = 50,
                HeightMicrometers = 30
            };

            group.AddChild(child);
        }

        return group;
    }
}
