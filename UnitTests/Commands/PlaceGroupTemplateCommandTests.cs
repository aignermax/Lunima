using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.LightCalculation;
using Shouldly;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Unit tests for PlaceGroupTemplateCommand.
/// Tests group template instantiation, placement, and undo/redo.
/// </summary>
public class PlaceGroupTemplateCommandTests : IDisposable
{
    private readonly string _testLibraryPath;
    private readonly GroupLibraryManager _libraryManager;
    private readonly DesignCanvasViewModel _canvas;

    public PlaceGroupTemplateCommandTests()
    {
        _testLibraryPath = Path.Combine(Path.GetTempPath(), $"PlaceGroupTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testLibraryPath);
        _libraryManager = new GroupLibraryManager(_testLibraryPath);
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
    public void TryCreate_WithValidTemplate_ReturnsCommand()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 2);
        var template = _libraryManager.SaveTemplate(group, "Test Template");
        template.TemplateGroup = group;

        // Act
        var cmd = PlaceGroupTemplateCommand.TryCreate(_canvas, _libraryManager, template, 100, 100);

        // Assert
        cmd.ShouldNotBeNull();
        cmd.Description.ShouldContain("Test Template");
    }

    [Fact]
    public void TryCreate_WithNullTemplateGroup_ReturnsNull()
    {
        // Arrange
        var template = new GroupTemplate
        {
            Name = "Empty Template",
            TemplateGroup = null
        };

        // Act
        var cmd = PlaceGroupTemplateCommand.TryCreate(_canvas, _libraryManager, template, 100, 100);

        // Assert
        cmd.ShouldBeNull();
    }

    [Fact]
    public void Execute_PlacesGroupOnCanvas()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 2);
        var template = _libraryManager.SaveTemplate(group, "Test Template");
        template.TemplateGroup = group;

        var cmd = PlaceGroupTemplateCommand.TryCreate(_canvas, _libraryManager, template, 100, 100);
        cmd.ShouldNotBeNull();

        // Act
        cmd.Execute();

        // Assert
        _canvas.Components.Count.ShouldBe(1); // Group is added as a component

        var placedGroupVm = _canvas.Components[0];
        placedGroupVm.ShouldNotBeNull();
        placedGroupVm.Component.ShouldBeOfType<ComponentGroup>();

        var placedGroup = (ComponentGroup)placedGroupVm.Component;
        placedGroup.ChildComponents.Count.ShouldBe(2);
    }

    [Fact]
    public void Execute_CreatesDeepCopyWithNewIds()
    {
        // Arrange
        var originalGroup = CreateTestGroup("Original", 2);
        var template = _libraryManager.SaveTemplate(originalGroup, "Test Template");
        template.TemplateGroup = originalGroup;

        var cmd = PlaceGroupTemplateCommand.TryCreate(_canvas, _libraryManager, template, 100, 100);
        cmd.ShouldNotBeNull();

        // Act
        cmd.Execute();

        // Assert
        var placedGroup = (ComponentGroup)_canvas.Components[0].Component;
        placedGroup.Identifier.ShouldNotBe(originalGroup.Identifier);
        placedGroup.ChildComponents[0].Identifier.ShouldNotBe(originalGroup.ChildComponents[0].Identifier);
        placedGroup.ChildComponents[1].Identifier.ShouldNotBe(originalGroup.ChildComponents[1].Identifier);
    }

    [Fact]
    public void Execute_MarksPlacedGroupAsNotPrefab()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 2);
        group.IsPrefab = true; // Mark as prefab
        var template = _libraryManager.SaveTemplate(group, "Test Template");
        template.TemplateGroup = group;

        var cmd = PlaceGroupTemplateCommand.TryCreate(_canvas, _libraryManager, template, 100, 100);
        cmd.ShouldNotBeNull();

        // Act
        cmd.Execute();

        // Assert
        var placedGroup = (ComponentGroup)_canvas.Components[0].Component;
        placedGroup.IsPrefab.ShouldBeFalse(); // Instance should not be a prefab
    }

    [Fact]
    public void Undo_RemovesPlacedGroup()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 2);
        var template = _libraryManager.SaveTemplate(group, "Test Template");
        template.TemplateGroup = group;

        var cmd = PlaceGroupTemplateCommand.TryCreate(_canvas, _libraryManager, template, 100, 100);
        cmd.ShouldNotBeNull();
        cmd.Execute();

        // Act
        cmd.Undo();

        // Assert
        _canvas.Components.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_Undo_Execute_PlacesGroupAgain()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 2);
        var template = _libraryManager.SaveTemplate(group, "Test Template");
        template.TemplateGroup = group;

        var cmd = PlaceGroupTemplateCommand.TryCreate(_canvas, _libraryManager, template, 100, 100);
        cmd.ShouldNotBeNull();

        // Act - Execute, Undo, Execute again
        cmd.Execute();
        var firstGroupId = ((ComponentGroup)_canvas.Components[0].Component).Identifier;

        cmd.Undo();
        _canvas.Components.Count.ShouldBe(0);

        cmd.Execute();

        // Assert - Should place the same group instance again
        _canvas.Components.Count.ShouldBe(1);
        var placedGroup = (ComponentGroup)_canvas.Components[0].Component;
        placedGroup.ShouldNotBeNull();
        // The group ID should be the same since we're re-executing the same command
        placedGroup.Identifier.ShouldBe(firstGroupId);
    }

    [Fact]
    public void TryCreate_WithNoSpaceAvailable_ReturnsNull()
    {
        // Arrange
        var group = CreateTestGroup("LargeGroup", 2);
        group.WidthMicrometers = 20000; // Larger than chip
        group.HeightMicrometers = 20000;

        var template = _libraryManager.SaveTemplate(group, "Large Template");
        template.TemplateGroup = group;
        template.WidthMicrometers = 20000;
        template.HeightMicrometers = 20000;

        // Act
        var cmd = PlaceGroupTemplateCommand.TryCreate(_canvas, _libraryManager, template, 0, 0);

        // Assert
        cmd.ShouldBeNull();
    }

    [Fact]
    public void Execute_AddsGroupComponentViewModelToCanvas()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 3);
        var template = _libraryManager.SaveTemplate(group, "Test Template");
        template.TemplateGroup = group;

        var cmd = PlaceGroupTemplateCommand.TryCreate(_canvas, _libraryManager, template, 100, 100);
        cmd.ShouldNotBeNull();

        // Act
        cmd.Execute();

        // Assert
        _canvas.Components.Count.ShouldBe(1); // Only the group is added
        var vm = _canvas.Components[0];
        vm.ShouldNotBeNull();
        vm.Component.ShouldNotBeNull();
        vm.Component.ShouldBeOfType<ComponentGroup>();
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
                new Dictionary<int, SMatrix>(),
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
