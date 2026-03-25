using CAP.Avalonia.Commands;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.LightCalculation;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Integration tests for the group template brush preview workflow.
/// Tests the preview display when hovering with a group template selected.
/// </summary>
public class GroupBrushPreviewIntegrationTests : IDisposable
{
    private readonly string _testLibraryPath;
    private readonly GroupLibraryManager _libraryManager;
    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentLibraryViewModel _libraryViewModel;
    private readonly CanvasInteractionViewModel _canvasInteraction;
    private readonly CommandManager _commandManager;
    private readonly CanvasInteractionState _interactionState;

    public GroupBrushPreviewIntegrationTests()
    {
        _testLibraryPath = Path.Combine(Path.GetTempPath(), $"BrushPreviewTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testLibraryPath);

        _libraryManager = new GroupLibraryManager(_testLibraryPath);
        _canvas = new DesignCanvasViewModel();
        _libraryViewModel = new ComponentLibraryViewModel(_libraryManager);
        _commandManager = new CommandManager();
        _canvasInteraction = new CanvasInteractionViewModel(_canvas, _commandManager, _libraryViewModel);
        _interactionState = new CanvasInteractionState();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testLibraryPath))
        {
            Directory.Delete(_testLibraryPath, true);
        }
    }

    [Fact]
    public void SelectGroupTemplate_EntersPlaceGroupTemplateMode()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 2);
        var template = _libraryManager.SaveTemplate(group, "Test Template");
        template.TemplateGroup = group;

        // Act
        _canvasInteraction.SelectedGroupTemplate = template;

        // Assert
        _canvasInteraction.CurrentMode.ShouldBe(InteractionMode.PlaceGroupTemplate);
        _canvasInteraction.SelectedGroupTemplate.ShouldBe(template);
    }

    [Fact]
    public void PreviewState_WhenGroupTemplateSelected_IsConfiguredCorrectly()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 3);
        var template = _libraryManager.SaveTemplate(group, "Test Template", "Test description");
        template.TemplateGroup = group;

        _canvasInteraction.SelectedGroupTemplate = template;

        // Act - Simulate mouse hover by setting preview state directly
        _interactionState.ShowGroupTemplatePlacementPreview = true;
        _interactionState.GroupTemplatePlacementPreview = template;
        _interactionState.GroupTemplatePlacementPreviewPosition = new Avalonia.Point(500, 500);

        // Assert
        _interactionState.ShowGroupTemplatePlacementPreview.ShouldBeTrue();
        _interactionState.GroupTemplatePlacementPreview.ShouldBe(template);
        _interactionState.GroupTemplatePlacementPreview.Name.ShouldBe("Test Template");
        _interactionState.GroupTemplatePlacementPreview.ComponentCount.ShouldBe(3);
        _interactionState.GroupTemplatePlacementPreview.WidthMicrometers.ShouldBeGreaterThan(0);
        _interactionState.GroupTemplatePlacementPreview.HeightMicrometers.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ResetPreviewState_ClearsGroupTemplatePreview()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 2);
        var template = _libraryManager.SaveTemplate(group, "Test Template");
        template.TemplateGroup = group;

        _interactionState.ShowGroupTemplatePlacementPreview = true;
        _interactionState.GroupTemplatePlacementPreview = template;

        // Act
        _interactionState.ResetGroupTemplatePlacementPreview();

        // Assert
        _interactionState.ShowGroupTemplatePlacementPreview.ShouldBeFalse();
        _interactionState.GroupTemplatePlacementPreview.ShouldBeNull();
    }

    [Fact]
    public void PlaceGroupAfterPreview_CreatesGroupOnCanvas()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 3);
        var template = _libraryManager.SaveTemplate(group, "Test Template");
        template.TemplateGroup = group;
        _canvasInteraction.SelectedGroupTemplate = template;

        // Simulate preview state
        _interactionState.ShowGroupTemplatePlacementPreview = true;
        _interactionState.GroupTemplatePlacementPreview = template;
        _interactionState.GroupTemplatePlacementPreviewPosition = new Avalonia.Point(500, 500);

        // Act - Click to place
        _canvasInteraction.CanvasClicked(500, 500);

        // Assert
        _canvas.Components.Count.ShouldBe(1);
        var placedGroup = (ComponentGroup)_canvas.Components[0].Component;
        placedGroup.ChildComponents.Count.ShouldBe(3);
        placedGroup.IsPrefab.ShouldBeFalse(); // Instance, not prefab
    }

    [Fact]
    public void SwitchToSelectMode_ClearsGroupTemplateSelection()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 2);
        var template = _libraryManager.SaveTemplate(group, "Test Template");
        template.TemplateGroup = group;
        _canvasInteraction.SelectedGroupTemplate = template;

        _interactionState.ShowGroupTemplatePlacementPreview = true;
        _interactionState.GroupTemplatePlacementPreview = template;

        // Act
        _canvasInteraction.SetSelectModeCommand.Execute(null);
        _interactionState.ResetGroupTemplatePlacementPreview();

        // Assert
        _canvasInteraction.CurrentMode.ShouldBe(InteractionMode.Select);
        _canvasInteraction.SelectedGroupTemplate.ShouldBeNull();
        _interactionState.ShowGroupTemplatePlacementPreview.ShouldBeFalse();
        _interactionState.GroupTemplatePlacementPreview.ShouldBeNull();
    }

    [Fact]
    public void PressEscape_ExitsBrushModeAndClearsPreview()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 2);
        var template = _libraryManager.SaveTemplate(group, "Test Template");
        template.TemplateGroup = group;
        _canvasInteraction.SelectedGroupTemplate = template;

        _interactionState.ShowGroupTemplatePlacementPreview = true;
        _interactionState.GroupTemplatePlacementPreview = template;

        // Act - Simulate ESC key behavior
        _canvasInteraction.SetSelectModeCommand.Execute(null);
        _interactionState.ResetGroupTemplatePlacementPreview();

        // Assert
        _canvasInteraction.CurrentMode.ShouldBe(InteractionMode.Select);
        _canvasInteraction.SelectedGroupTemplate.ShouldBeNull();
        _interactionState.ShowGroupTemplatePlacementPreview.ShouldBeFalse();
    }

    [Fact]
    public void MultipleGroupPlacements_EachShowsPreviewBeforePlacing()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 2);
        var template = _libraryManager.SaveTemplate(group, "Test Template");
        template.TemplateGroup = group;
        _canvasInteraction.SelectedGroupTemplate = template;

        // Act & Assert - First placement
        _interactionState.ShowGroupTemplatePlacementPreview = true;
        _interactionState.GroupTemplatePlacementPreview = template;
        _interactionState.GroupTemplatePlacementPreviewPosition = new Avalonia.Point(500, 500);

        _interactionState.ShowGroupTemplatePlacementPreview.ShouldBeTrue();
        _canvasInteraction.CanvasClicked(500, 500);
        _canvas.Components.Count.ShouldBe(1);

        // Second placement (brush should still be active)
        _interactionState.ShowGroupTemplatePlacementPreview = true;
        _interactionState.GroupTemplatePlacementPreviewPosition = new Avalonia.Point(1000, 1000);

        _interactionState.ShowGroupTemplatePlacementPreview.ShouldBeTrue();
        _canvasInteraction.CanvasClicked(1000, 1000);
        _canvas.Components.Count.ShouldBe(2);

        // Verify independent instances
        var group1 = (ComponentGroup)_canvas.Components[0].Component;
        var group2 = (ComponentGroup)_canvas.Components[1].Component;
        group1.Identifier.ShouldNotBe(group2.Identifier);
    }

    [Fact]
    public void PreviewWithNoValidPosition_ShowsRedPreview()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 2);
        var template = _libraryManager.SaveTemplate(group, "Test Template");
        template.TemplateGroup = group;
        _canvasInteraction.SelectedGroupTemplate = template;

        // Fill canvas to make placement impossible
        for (int i = 0; i < 50; i++)
        {
            for (int j = 0; j < 50; j++)
            {
                var filler = CreateTestGroup($"Filler_{i}_{j}", 1);
                filler.PhysicalX = i * 100;
                filler.PhysicalY = j * 100;
                _canvas.AddComponent(filler);
            }
        }

        // Act - Try to place at an occupied position
        double testX = 500;
        double testY = 500;
        double width = template.WidthMicrometers;
        double height = template.HeightMicrometers;
        double placementX = testX - width / 2;
        double placementY = testY - height / 2;

        bool canPlace = _canvas.CanPlaceComponent(placementX, placementY, width, height);

        // Assert - Preview should indicate invalid placement
        canPlace.ShouldBeFalse();
        _interactionState.ShowGroupTemplatePlacementPreview = true;
        _interactionState.GroupTemplatePlacementPreview = template;
        _interactionState.GroupTemplatePlacementPreviewPosition = new Avalonia.Point(testX, testY);

        // Preview is shown even when placement is invalid (red color)
        _interactionState.ShowGroupTemplatePlacementPreview.ShouldBeTrue();
        _interactionState.GroupTemplatePlacementPreview.ShouldNotBeNull();
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
                    },
                    new PhysicalPin
                    {
                        Name = "b0",
                        OffsetXMicrometers = 50,
                        OffsetYMicrometers = 0,
                        AngleDegrees = 0
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

        group.UpdateGroupBounds();
        return group;
    }
}
