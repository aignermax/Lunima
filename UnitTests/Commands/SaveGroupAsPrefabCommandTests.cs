using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Unit tests for SaveGroupAsPrefabCommand.
/// Tests that groups are explicitly saved as prefabs only when user requests it.
/// </summary>
public class SaveGroupAsPrefabCommandTests : IDisposable
{
    private readonly string _testLibraryPath;
    private readonly GroupLibraryManager _libraryManager;
    private readonly ComponentLibraryViewModel _libraryViewModel;
    private readonly GroupPreviewGenerator _previewGenerator;

    public SaveGroupAsPrefabCommandTests()
    {
        _testLibraryPath = Path.Combine(Path.GetTempPath(), $"PrefabTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testLibraryPath);
        _libraryManager = new GroupLibraryManager(_testLibraryPath);
        _libraryViewModel = new ComponentLibraryViewModel(_libraryManager);
        _previewGenerator = new GroupPreviewGenerator();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testLibraryPath))
        {
            Directory.Delete(_testLibraryPath, true);
        }
    }

    [Fact]
    public void Execute_MarksGroupAsPrefab()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup");
        group.IsPrefab.ShouldBeFalse();

        var cmd = new SaveGroupAsPrefabCommand(
            _libraryViewModel,
            _previewGenerator,
            group,
            "My Prefab",
            "A test prefab");

        // Act
        cmd.Execute();

        // Assert
        group.IsPrefab.ShouldBeTrue();
    }

    [Fact]
    public void Execute_AddsTemplateToLibraryViewModel()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup");
        var initialCount = _libraryViewModel.UserGroups.Count;

        var cmd = new SaveGroupAsPrefabCommand(
            _libraryViewModel,
            _previewGenerator,
            group,
            "My Prefab");

        // Act
        cmd.Execute();

        // Assert
        _libraryViewModel.UserGroups.Count.ShouldBe(initialCount + 1);
        _libraryViewModel.UserGroups.Last().Template.Name.ShouldBe("My Prefab");
    }

    [Fact]
    public void Undo_RemovesTemplateFromLibrary()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup");
        var cmd = new SaveGroupAsPrefabCommand(
            _libraryViewModel,
            _previewGenerator,
            group,
            "My Prefab");

        cmd.Execute();
        var initialCount = _libraryViewModel.UserGroups.Count;

        // Act
        cmd.Undo();

        // Assert
        _libraryViewModel.UserGroups.Count.ShouldBe(initialCount - 1);
        group.IsPrefab.ShouldBeFalse();
    }

    [Fact]
    public void Execute_WithDescription_SavesDescription()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup");
        var cmd = new SaveGroupAsPrefabCommand(
            _libraryViewModel,
            _previewGenerator,
            group,
            "My Prefab",
            "This is a detailed description");

        // Act
        cmd.Execute();

        // Assert
        var template = _libraryViewModel.UserGroups.Last().Template;
        template.Description.ShouldBe("This is a detailed description");
    }

    [Fact]
    public void Description_ReturnsCorrectText()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup");
        var cmd = new SaveGroupAsPrefabCommand(
            _libraryViewModel,
            _previewGenerator,
            group,
            "My Prefab Name");

        // Act & Assert
        cmd.Description.ShouldBe("Save 'My Prefab Name' as prefab");
    }

    /// <summary>
    /// Creates a test ComponentGroup with child components.
    /// </summary>
    private ComponentGroup CreateTestGroup(string name)
    {
        var group = new ComponentGroup(name)
        {
            PhysicalX = 0,
            PhysicalY = 0
        };

        for (int i = 0; i < 2; i++)
        {
            var child = new Component(
                new Dictionary<int, SMatrix>(),
                new List<Slider>(),
                "test_component",
                "",
                new Part[1, 1] { { new Part() } },
                -1,
                $"comp_{i}_{Guid.NewGuid():N}",
                DiscreteRotation.R0,
                new List<PhysicalPin>())
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
