using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Integration tests for the complete group prefab workflow.
/// Tests the interaction between CreateGroupCommand and SaveGroupAsPrefabCommand.
/// Verifies that groups are NOT auto-saved to library, only when explicitly saved as prefabs.
/// </summary>
public class GroupPrefabWorkflowTests : IDisposable
{
    private readonly string _testLibraryPath;
    private readonly GroupLibraryManager _libraryManager;
    private readonly ComponentLibraryViewModel _libraryViewModel;
    private readonly GroupPreviewGenerator _previewGenerator;
    private readonly DesignCanvasViewModel _canvas;

    public GroupPrefabWorkflowTests()
    {
        _testLibraryPath = Path.Combine(Path.GetTempPath(), $"GroupWorkflowTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testLibraryPath);
        _libraryManager = new GroupLibraryManager(_testLibraryPath);
        _libraryViewModel = new ComponentLibraryViewModel(_libraryManager);
        _previewGenerator = new GroupPreviewGenerator();
        _canvas = new DesignCanvasViewModel();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testLibraryPath))
        {
            Directory.Delete(_testLibraryPath, true);
        }
    }

    [Fact]
    public void CreateGroupCommand_DoesNotAutoSaveToLibrary()
    {
        // Arrange
        var comp1 = CreateTestComponent("comp1", 0, 0);
        var comp2 = CreateTestComponent("comp2", 100, 0);

        var vm1 = _canvas.AddComponent(comp1);
        var vm2 = _canvas.AddComponent(comp2);

        var initialLibraryCount = _libraryViewModel.UserGroups.Count;

        // Act - Create group (without saving to library)
        var createCmd = new CreateGroupCommand(_canvas, new List<ComponentViewModel> { vm1, vm2 });
        createCmd.Execute();

        // Assert - Library should NOT be updated
        _libraryViewModel.UserGroups.Count.ShouldBe(initialLibraryCount);
    }

    [Fact]
    public void FullWorkflow_CreateThenSaveAsPrefab_OnlyPrefabInLibrary()
    {
        // Arrange
        var comp1 = CreateTestComponent("comp1", 0, 0);
        var comp2 = CreateTestComponent("comp2", 100, 0);

        var vm1 = _canvas.AddComponent(comp1);
        var vm2 = _canvas.AddComponent(comp2);

        var initialLibraryCount = _libraryViewModel.UserGroups.Count;

        // Act 1 - Create group
        var createCmd = new CreateGroupCommand(_canvas, new List<ComponentViewModel> { vm1, vm2 });
        createCmd.Execute();

        // Assert 1 - Not in library yet
        _libraryViewModel.UserGroups.Count.ShouldBe(initialLibraryCount);

        // Act 2 - Explicitly save as prefab
        var groupVm = _canvas.Components.FirstOrDefault(c => c.Component is ComponentGroup);
        groupVm.ShouldNotBeNull();

        var group = (ComponentGroup)groupVm.Component;
        var saveCmd = new SaveGroupAsPrefabCommand(
            _libraryViewModel,
            _previewGenerator,
            group,
            "My Reusable Prefab",
            "A test prefab");

        saveCmd.Execute();

        // Assert 2 - Now in library and marked as prefab
        _libraryViewModel.UserGroups.Count.ShouldBe(initialLibraryCount + 1);
        group.IsPrefab.ShouldBeTrue();
        _libraryViewModel.UserGroups.Last().Name.ShouldBe("My Reusable Prefab");
    }

    [Fact]
    public void CreateMultipleGroups_NoneAutoSaved()
    {
        // Arrange
        var comp1 = CreateTestComponent("comp1", 0, 0);
        var comp2 = CreateTestComponent("comp2", 100, 0);
        var comp3 = CreateTestComponent("comp3", 200, 0);
        var comp4 = CreateTestComponent("comp4", 300, 0);

        var vm1 = _canvas.AddComponent(comp1);
        var vm2 = _canvas.AddComponent(comp2);
        var vm3 = _canvas.AddComponent(comp3);
        var vm4 = _canvas.AddComponent(comp4);

        var initialLibraryCount = _libraryViewModel.UserGroups.Count;

        // Act - Create two groups
        var createCmd1 = new CreateGroupCommand(_canvas, new List<ComponentViewModel> { vm1, vm2 });
        createCmd1.Execute();

        var createCmd2 = new CreateGroupCommand(_canvas, new List<ComponentViewModel> { vm3, vm4 });
        createCmd2.Execute();

        // Assert - Neither group should be in library
        _libraryViewModel.UserGroups.Count.ShouldBe(initialLibraryCount);
        _canvas.Components.Count(c => c.Component is ComponentGroup).ShouldBe(2);
    }

    [Fact]
    public void SaveGroupAsPrefab_ThenUndo_RemovesFromLibrary()
    {
        // Arrange
        var comp1 = CreateTestComponent("comp1", 0, 0);
        var comp2 = CreateTestComponent("comp2", 100, 0);

        var vm1 = _canvas.AddComponent(comp1);
        var vm2 = _canvas.AddComponent(comp2);

        var createCmd = new CreateGroupCommand(_canvas, new List<ComponentViewModel> { vm1, vm2 });
        createCmd.Execute();

        var group = (ComponentGroup)_canvas.Components.First(c => c.Component is ComponentGroup).Component;
        var saveCmd = new SaveGroupAsPrefabCommand(
            _libraryViewModel,
            _previewGenerator,
            group,
            "Test Prefab");

        saveCmd.Execute();
        var countAfterSave = _libraryViewModel.UserGroups.Count;

        // Act - Undo the save
        saveCmd.Undo();

        // Assert
        _libraryViewModel.UserGroups.Count.ShouldBe(countAfterSave - 1);
        group.IsPrefab.ShouldBeFalse();
    }

    [Fact]
    public void GroupIsPrefabProperty_DefaultsFalse()
    {
        // Arrange
        var comp1 = CreateTestComponent("comp1", 0, 0);
        var comp2 = CreateTestComponent("comp2", 100, 0);

        var vm1 = _canvas.AddComponent(comp1);
        var vm2 = _canvas.AddComponent(comp2);

        // Act
        var createCmd = new CreateGroupCommand(_canvas, new List<ComponentViewModel> { vm1, vm2 });
        createCmd.Execute();

        // Assert
        var group = (ComponentGroup)_canvas.Components.First(c => c.Component is ComponentGroup).Component;
        group.IsPrefab.ShouldBeFalse();
    }

    /// <summary>
    /// Creates a test component.
    /// </summary>
    private Component CreateTestComponent(string id, double x, double y)
    {
        return new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "test_component",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            id,
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
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = 50,
            HeightMicrometers = 30
        };
    }
}
