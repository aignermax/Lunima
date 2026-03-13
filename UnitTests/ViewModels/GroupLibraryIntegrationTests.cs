using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Integration tests for group library workflow.
/// Tests Core (GroupLibraryManager) + ViewModel (ComponentLibraryViewModel) interaction.
/// </summary>
public class GroupLibraryIntegrationTests : IDisposable
{
    private readonly string _testLibraryPath;
    private readonly GroupLibraryManager _libraryManager;
    private readonly ComponentLibraryViewModel _libraryViewModel;
    private readonly GroupPreviewGenerator _previewGenerator;

    public GroupLibraryIntegrationTests()
    {
        _testLibraryPath = Path.Combine(Path.GetTempPath(), $"GroupLibraryIntegTests_{Guid.NewGuid():N}");
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
    public void SaveGroupToLibraryCommand_AddsTemplateToViewModel()
    {
        // Arrange
        var group = CreateTestGroup("Test Group", 3);
        var command = new SaveGroupToLibraryCommand(
            _libraryViewModel,
            _previewGenerator,
            group,
            "My Saved Group",
            "A test group with 3 components");

        // Act
        command.Execute();

        // Assert
        _libraryViewModel.UserGroups.Count.ShouldBe(1);
        var template = _libraryViewModel.UserGroups.First();
        template.Name.ShouldBe("My Saved Group");
        template.Description.ShouldBe("A test group with 3 components");
        template.ComponentCount.ShouldBe(3);
    }

    [Fact]
    public void SaveGroupToLibraryCommand_Undo_RemovesTemplate()
    {
        // Arrange
        var group = CreateTestGroup("Test Group", 2);
        var command = new SaveGroupToLibraryCommand(
            _libraryViewModel,
            _previewGenerator,
            group,
            "Temporary Group");

        command.Execute();
        _libraryViewModel.UserGroups.Count.ShouldBe(1);

        // Act
        command.Undo();

        // Assert
        _libraryViewModel.UserGroups.Count.ShouldBe(0);
    }

    [Fact]
    public void LoadGroups_PopulatesViewModelCollections()
    {
        // Arrange
        var userGroup = CreateTestGroup("UserGroup", 2);
        var pdkGroup = CreateTestGroup("PdkGroup", 1);
        _libraryManager.SaveTemplate(userGroup, "User Group 1", null, "User");
        _libraryManager.SaveTemplate(pdkGroup, "PDK Macro 1", null, "PDK");

        // Act
        _libraryViewModel.LoadGroupsCommand.Execute(null);

        // Assert
        _libraryViewModel.UserGroups.Count.ShouldBe(1);
        _libraryViewModel.PdkGroups.Count.ShouldBe(1);
        _libraryViewModel.StatusText.ShouldContain("1 user group");
        _libraryViewModel.StatusText.ShouldContain("1 PDK macro");
    }

    [Fact]
    public void RemoveTemplate_RemovesFromViewModelAndDisk()
    {
        // Arrange
        var group = CreateTestGroup("Group to Remove", 1);
        _libraryManager.SaveTemplate(group, "Remove Me");
        _libraryViewModel.LoadGroupsCommand.Execute(null);
        var template = _libraryViewModel.UserGroups.First();
        var filePath = template.FilePath;

        // Act
        _libraryViewModel.RemoveTemplateCommand.Execute(template);

        // Assert
        _libraryViewModel.UserGroups.ShouldNotContain(template);
        if (filePath != null)
        {
            File.Exists(filePath).ShouldBeFalse();
        }
    }

    [Fact]
    public void DuplicateTemplate_CreatesNewTemplate()
    {
        // Arrange
        var group = CreateTestGroup("Original", 2);
        _libraryManager.SaveTemplate(group, "Original Group", "Original description");
        _libraryViewModel.LoadGroupsCommand.Execute(null);
        var original = _libraryViewModel.UserGroups.First();
        original.TemplateGroup = group; // Ensure template group is loaded

        // Act
        _libraryViewModel.DuplicateTemplateCommand.Execute(original);

        // Assert
        _libraryViewModel.UserGroups.Count.ShouldBe(2);
        var duplicate = _libraryViewModel.UserGroups.FirstOrDefault(t => t.Name.Contains("Copy"));
        duplicate.ShouldNotBeNull();
        duplicate!.ComponentCount.ShouldBe(2);
    }

    [Fact]
    public void InstantiateTemplate_CreatesIndependentCopy()
    {
        // Arrange
        var originalGroup = CreateTestGroup("Template", 2);
        var template = _libraryManager.SaveTemplate(originalGroup, "Reusable Template");
        template.TemplateGroup = originalGroup;

        // Act
        var instance1 = _libraryManager.InstantiateTemplate(template, 0, 0);
        var instance2 = _libraryManager.InstantiateTemplate(template, 500, 500);

        // Assert
        instance1.Identifier.ShouldNotBe(instance2.Identifier);
        instance1.ChildComponents[0].Identifier.ShouldNotBe(instance2.ChildComponents[0].Identifier);
        instance1.PhysicalX.ShouldBe(0);
        instance2.PhysicalX.ShouldBe(500);
    }

    [Fact]
    public void StatusText_UpdatesWithGroupCounts()
    {
        // Arrange - empty library
        _libraryViewModel.LoadGroupsCommand.Execute(null);
        _libraryViewModel.StatusText.ShouldBe("No saved groups");

        // Act - add user groups
        var group1 = CreateTestGroup("Group1", 1);
        _libraryManager.SaveTemplate(group1, "User Group 1");
        _libraryViewModel.LoadGroupsCommand.Execute(null);

        // Assert
        _libraryViewModel.StatusText.ShouldContain("1 user group");

        // Act - add PDK group
        var group2 = CreateTestGroup("Group2", 1);
        _libraryManager.SaveTemplate(group2, "PDK Macro 1", null, "PDK");
        _libraryViewModel.LoadGroupsCommand.Execute(null);

        // Assert
        _libraryViewModel.StatusText.ShouldContain("1 user group");
        _libraryViewModel.StatusText.ShouldContain("1 PDK macro");
    }

    [Fact]
    public void CreateGroupCommand_WithLibraryViewModel_AutoSavesToLibrary()
    {
        // Arrange
        var canvas = new CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel();
        var comp1 = CreateTestComponent("comp1");
        var comp2 = CreateTestComponent("comp2");
        var compVm1 = canvas.AddComponent(comp1);
        var compVm2 = canvas.AddComponent(comp2);

        var selectedComponents = new List<CAP.Avalonia.ViewModels.Canvas.ComponentViewModel> { compVm1, compVm2 };
        var command = new CreateGroupCommand(canvas, selectedComponents, _libraryViewModel, _previewGenerator);

        // Act
        command.Execute();

        // Assert - group should be auto-saved to library
        _libraryViewModel.UserGroups.Count.ShouldBe(1);
        var savedTemplate = _libraryViewModel.UserGroups.First();
        savedTemplate.ComponentCount.ShouldBe(2);
        savedTemplate.Source.ShouldBe("User");
    }

    [Fact]
    public void CreateGroupCommand_WithoutLibraryViewModel_DoesNotSave()
    {
        // Arrange
        var canvas = new CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel();
        var comp1 = CreateTestComponent("comp1");
        var comp2 = CreateTestComponent("comp2");
        var compVm1 = canvas.AddComponent(comp1);
        var compVm2 = canvas.AddComponent(comp2);

        var selectedComponents = new List<CAP.Avalonia.ViewModels.Canvas.ComponentViewModel> { compVm1, compVm2 };
        var command = new CreateGroupCommand(canvas, selectedComponents, null, null);

        // Act
        command.Execute();

        // Assert - no crash, group is created but not saved to library
        _libraryViewModel.UserGroups.Count.ShouldBe(0);
        canvas.Components.Count.ShouldBe(1); // Only the group component
    }

    [Fact]
    public void CreateGroupCommand_Undo_RemovesSavedTemplate()
    {
        // Arrange
        var canvas = new CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel();
        var comp1 = CreateTestComponent("comp1");
        var comp2 = CreateTestComponent("comp2");
        var compVm1 = canvas.AddComponent(comp1);
        var compVm2 = canvas.AddComponent(comp2);

        var selectedComponents = new List<CAP.Avalonia.ViewModels.Canvas.ComponentViewModel> { compVm1, compVm2 };
        var command = new CreateGroupCommand(canvas, selectedComponents, _libraryViewModel, _previewGenerator);

        command.Execute();
        _libraryViewModel.UserGroups.Count.ShouldBe(1);

        // Act
        command.Undo();

        // Assert - template removed from library
        _libraryViewModel.UserGroups.Count.ShouldBe(0);
        // Individual components restored
        canvas.Components.Count.ShouldBe(2);
    }

    /// <summary>
    /// Creates a simple test component with minimal configuration.
    /// </summary>
    private Component CreateTestComponent(string identifier)
    {
        return new Component(
            new Dictionary<int, CAP_Core.LightCalculation.SMatrix>(),
            new List<Slider>(),
            "test_component",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            identifier,
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
            PhysicalX = 0,
            PhysicalY = 0,
            WidthMicrometers = 50,
            HeightMicrometers = 30
        };
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
