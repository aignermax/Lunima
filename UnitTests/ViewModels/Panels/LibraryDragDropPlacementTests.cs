using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.Components.Process;
using CAP_Core.LightCalculation;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels.Panels;

/// <summary>
/// True drag&amp;drop placement from the library (issue #1157): releasing a dragged
/// library template over the canvas drops the instance at the release point and returns
/// the canvas to Select mode — the drag is a one-shot gesture, unlike click-to-place
/// which stays armed. Rejected drops keep the same policy guards as click-to-place.
/// </summary>
public class LibraryDragDropPlacementTests : IDisposable
{
    private static readonly ProcessGroup SoiGroup = new(
        "SOI 220", new ProcessFingerprint("Si", 220, "SiO2", 1550, "SOI 220"), new[] { "Demo" });

    private readonly string _testLibraryPath;
    private readonly GroupLibraryManager _libraryManager;
    private readonly DesignCanvasViewModel _canvas;
    private readonly CanvasInteractionViewModel _interaction;
    private readonly CommandManager _commandManager;

    public LibraryDragDropPlacementTests()
    {
        _testLibraryPath = Path.Combine(Path.GetTempPath(), $"LibraryDragDropTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testLibraryPath);

        _libraryManager = new GroupLibraryManager(_testLibraryPath);
        _canvas = new DesignCanvasViewModel();
        _commandManager = new CommandManager();
        _interaction = new CanvasInteractionViewModel(
            _canvas, _commandManager, new ComponentLibraryViewModel(_libraryManager));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testLibraryPath))
            Directory.Delete(_testLibraryPath, true);
    }

    [Fact]
    public void DropComponentTemplate_PlacesInstanceAtReleasePoint()
    {
        var template = BuildTemplate("Demo", width: 100, height: 60);

        _interaction.DropComponentTemplateAt(template, 500, 300);

        _canvas.Components.Count.ShouldBe(1);
        var placed = _canvas.Components.Single();
        // The drop point is the template's center, exactly like the click-to-place ghost.
        placed.X.ShouldBe(450);
        placed.Y.ShouldBe(270);
    }

    [Fact]
    public void DropComponentTemplate_ReturnsToSelectModeAndClearsTemplate()
    {
        var template = BuildTemplate("Demo");

        _interaction.DropComponentTemplateAt(template, 500, 300);

        _interaction.CurrentMode.ShouldBe(InteractionMode.Select,
            "drag&drop is a one-shot gesture — the canvas must not stay armed in place mode");
        _interaction.SelectedTemplate.ShouldBeNull();
    }

    [Fact]
    public void DropComponentTemplate_ReportsPlacementInStatusBar()
    {
        var template = BuildTemplate("Demo");
        string? status = null;
        _interaction.UpdateStatus = s => status = s;

        _interaction.DropComponentTemplateAt(template, 500, 300);

        status.ShouldNotBeNull();
        status!.ShouldContain("Placed");
        status.ShouldContain(template.Name);
    }

    [Fact]
    public void DropComponentTemplate_IsUndoable()
    {
        var template = BuildTemplate("Demo");
        _interaction.DropComponentTemplateAt(template, 500, 300);
        _canvas.Components.Count.ShouldBe(1);

        _commandManager.Undo();

        _canvas.Components.Count.ShouldBe(0);
    }

    [Fact]
    public void DropComponentTemplate_FromForeignProcess_StaysBlocked()
    {
        _interaction.PlacementContext = new PlacementPolicyContext(
            () => ActiveProcessSelection.ForGroup(SoiGroup),
            () => Array.Empty<string>(),
            component => component.NazcaFunctionName == "member_func" ? "Demo" : "HHI-InP");

        var template = BuildTemplate("HHI-InP");
        string? status = null;
        _interaction.UpdateStatus = s => status = s;

        _interaction.DropComponentTemplateAt(template, 500, 300);

        _canvas.Components.Count.ShouldBe(0, "a foreign-process template must not be dropped");
        status.ShouldNotBeNull();
        _interaction.CurrentMode.ShouldBe(InteractionMode.Select,
            "even a rejected drop ends the drag gesture");
    }

    [Fact]
    public void DropGroupTemplate_PlacesGroupAndReturnsToSelectMode()
    {
        var group = BuildGroup("DropGroup", childCount: 2);
        var template = _libraryManager.SaveTemplate(group, "Drop Group");
        template.TemplateGroup = group;

        _interaction.DropGroupTemplateAt(template, 500, 500);

        _canvas.Components.Count.ShouldBe(1);
        var placedGroup = (ComponentGroup)_canvas.Components.Single().Component;
        placedGroup.ChildComponents.Count.ShouldBe(2);
        placedGroup.IsPrefab.ShouldBeFalse("the drop creates an instance, not a prefab");
        _interaction.CurrentMode.ShouldBe(InteractionMode.Select);
        _interaction.SelectedGroupTemplate.ShouldBeNull();
    }

    private static ComponentTemplate BuildTemplate(string pdkSource, double width = 100, double height = 60) => new()
    {
        Name = "TestComp",
        Category = "Test",
        PdkSource = pdkSource,
        WidthMicrometers = width,
        HeightMicrometers = height,
        NazcaFunctionName = "member_func",
        PinDefinitions = new[] { new PinDefinition("a", 0, 5, 180) },
        CreateSMatrix = pins =>
        {
            var ids = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
            return new SMatrix(ids, new List<(Guid, double)>());
        }
    };

    private static ComponentGroup BuildGroup(string name, int childCount)
    {
        var group = new ComponentGroup(name) { PhysicalX = 0, PhysicalY = 0 };
        for (int i = 0; i < childCount; i++)
        {
            group.AddChild(new Component(
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
            });
        }
        return group;
    }
}
