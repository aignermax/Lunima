using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Creation;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Shouldly;

namespace UnitTests.AI;

/// <summary>
/// Unit tests for <see cref="AiGridService"/> covering all grid operations.
/// Uses real (minimal) instances of dependencies with empty state.
/// </summary>
public class AiGridServiceTests
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly LeftPanelViewModel _leftPanel;
    private readonly SimulationService _simulationService;
    private readonly AiGridService _svc;

    public AiGridServiceTests()
    {
        _canvas = new DesignCanvasViewModel();
        _leftPanel = CreateMinimalLeftPanel(_canvas);
        _simulationService = new SimulationService();
        _svc = new AiGridService(_canvas, _leftPanel, _simulationService);
    }

    [Fact]
    public void GetGridState_EmptyCanvas_ReturnsValidJsonWithExpectedKeys()
    {
        var result = _svc.GetGridState();

        result.ShouldNotBeNullOrEmpty();
        result.ShouldContain("\"components\"");
        result.ShouldContain("\"connections\"");
        result.ShouldContain("\"available_types\"");
    }

    [Fact]
    public void GetGridState_EmptyCanvas_HasZeroComponents()
    {
        var result = _svc.GetGridState();

        // The components array should be empty
        result.ShouldContain("\"components\":[]");
    }

    [Fact]
    public void GetAvailableComponentTypes_NoTemplatesLoaded_ReturnsEmptyList()
    {
        var types = _svc.GetAvailableComponentTypes();

        types.ShouldNotBeNull();
        types.Count.ShouldBe(0);
    }

    [Fact]
    public async Task PlaceComponentAsync_UnknownType_ReturnsNotFoundMessage()
    {
        var result = await _svc.PlaceComponentAsync("NonExistentMMI", 100, 100);

        result.ShouldContain("not found");
        result.ShouldContain("NonExistentMMI");
    }

    [Fact]
    public async Task PlaceComponentAsync_UnknownType_IncludesAvailableTypesHint()
    {
        // With no templates loaded, the error hints at 0 available types
        var result = await _svc.PlaceComponentAsync("X", 200, 200);

        result.ShouldContain("not found");
    }

    [Fact]
    public async Task CreateConnectionAsync_UnknownFromComponent_ReturnsNotFoundMessage()
    {
        var result = await _svc.CreateConnectionAsync("MMI_1", "WG_2");

        result.ShouldContain("not found");
        result.ShouldContain("MMI_1");
    }

    [Fact]
    public async Task CreateConnectionAsync_UnknownToComponent_ReturnsNotFoundMessage()
    {
        // First register a fake component by adding it directly to the canvas
        var result = await _svc.CreateConnectionAsync("MissingFrom_1", "MissingTo_2");

        result.ShouldContain("not found");
    }

    [Fact]
    public async Task RunSimulationAsync_EmptyCanvas_ReturnsNoComponentsMessage()
    {
        var result = await _svc.RunSimulationAsync();

        result.ShouldContain("No components");
    }

    [Fact]
    public void GetLightValues_EmptyCanvas_ReturnsValidJsonWithConnectionsKey()
    {
        var result = _svc.GetLightValues();

        result.ShouldNotBeNullOrEmpty();
        result.ShouldContain("\"connections\"");
    }

    [Fact]
    public void ClearGrid_EmptyCanvas_ReportsZeroRemovedComponents()
    {
        var result = _svc.ClearGrid();

        result.ShouldContain("0");
    }

    [Fact]
    public void ClearGrid_AfterOperations_ReturnsSuccessMessage()
    {
        var result = _svc.ClearGrid();

        result.ShouldNotBeNullOrEmpty();
        result.ShouldContain("cleared");
    }

<<<<<<< HEAD
    // ── InspectGroup tests ──────────────────────────────────────────────────

    [Fact]
    public void InspectGroup_NonExistentId_ReturnsErrorMessage()
    {
        var result = _svc.InspectGroup("does_not_exist");

        result.ShouldContain("does_not_exist");
        result.ShouldNotStartWith("{");
    }

    [Fact]
    public void InspectGroup_PlainComponent_ReturnsNotAGroupMessage()
    {
        var comp = CreateTestComponent("plain_comp", 100, 100);
        _canvas.AddComponent(comp);

        var result = _svc.InspectGroup(comp.Identifier);

        result.ShouldContain("not a group");
    }

    [Fact]
    public void InspectGroup_Group_ReturnsValidJson()
    {
        var group = CreateGroupWithTwoChildren();

        var result = _svc.InspectGroup(group.Identifier);

        result.ShouldNotBeNullOrEmpty();
        var doc = JsonDocument.Parse(result); // must not throw
        doc.RootElement.GetProperty("group_id").GetString().ShouldBe(group.Identifier);
    }

    [Fact]
    public void InspectGroup_Group_ReturnsGroupNameAndChildCount()
    {
        var group = CreateGroupWithTwoChildren("MyMZI");

        var result = _svc.InspectGroup(group.Identifier);

        result.ShouldContain("\"group_name\"");
        result.ShouldContain("MyMZI");
        result.ShouldContain("\"total_child_count\":2");
    }

    [Fact]
    public void InspectGroup_Group_ChildComponentsIncludeTypeAndPosition()
    {
        var group = CreateGroupWithTwoChildren();

        var result = _svc.InspectGroup(group.Identifier);

        result.ShouldContain("\"child_components\"");
        result.ShouldContain("\"type\"");
        result.ShouldContain("\"position\"");
    }

    [Fact]
    public void InspectGroup_Group_ExternalPinsFieldPresent()
    {
        var group = CreateGroupWithTwoChildren();

        var result = _svc.InspectGroup(group.Identifier);

        result.ShouldContain("\"external_pins\"");
    }

    [Fact]
    public void InspectGroup_Group_InternalConnectionsFieldPresent()
    {
        var group = CreateGroupWithTwoChildren();

        var result = _svc.InspectGroup(group.Identifier);

        result.ShouldContain("\"internal_connections\"");
    }

    [Fact]
    public void InspectGroup_NestedGroup_ReportsNestedGroupCount()
    {
        // Arrange: outer group contains an inner group as one of its children
        var inner = new ComponentGroup("InnerGroup");
        var child = CreateTestComponent("child_in_inner", 50, 50);
        inner.AddChild(child);
        inner.Identifier = $"group_{Guid.NewGuid():N}";
        inner.PhysicalX = 100;
        inner.PhysicalY = 100;

        var outer = new ComponentGroup("OuterGroup");
        outer.AddChild(inner);
        outer.Identifier = $"group_{Guid.NewGuid():N}";
        outer.PhysicalX = 0;
        outer.PhysicalY = 0;

        _canvas.AddComponent(outer);

        var result = _svc.InspectGroup(outer.Identifier);

        result.ShouldContain("\"nested_group_count\":1");
    }

    /// <summary>
    /// Creates a group with two plain child components and places it on the canvas.
    /// </summary>
    private ComponentGroup CreateGroupWithTwoChildren(string groupName = "TestGroup")
    {
        var comp1 = CreateTestComponent("child1", 100, 100);
        var comp2 = CreateTestComponent("child2", 200, 100);

        var vm1 = _canvas.AddComponent(comp1);
        var vm2 = _canvas.AddComponent(comp2);

        var cmd = new CreateGroupCommand(_canvas, new[] { vm1, vm2 }.ToList());
        cmd.Execute();

        var groupVm = _canvas.Components[0];
        var group = (ComponentGroup)groupVm.Component;
        group.GroupName = groupName;
        return group;
    }

    private static Component CreateTestComponent(string identifier, double x, double y)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin> { new("west0", 0, MatterType.Light, RectSide.Left) });
        var allPins = Component.GetAllPins(parts).SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var matrix = new SMatrix(allPins, new());
        var connections = new Dictionary<int, SMatrix>
        {
            { StandardWaveLengths.RedNM, matrix }
        };
        return new Component(connections, new(), "stub", "", parts, 0, identifier, new DiscreteRotation())
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = 50,
            HeightMicrometers = 50,
            HumanReadableName = identifier
        };
    }

    // ── CopyComponentAsync ────────────────────────────────────────────────

    [Fact]
    public async Task CopyComponentAsync_UnknownSourceId_ReturnsNotFoundMessage()
    {
        var result = await _svc.CopyComponentAsync("nonexistent_1", 200, 200);

        result.ShouldContain("not found");
        result.ShouldContain("nonexistent_1");
    }

    [Fact]
    public async Task CopyComponentAsync_ValidComponent_CreatesNewComponentOnCanvas()
    {
        var component = TestComponentFactory.CreateBasicComponent();
        component.Identifier = "source_1";
        component.PhysicalX = 100;
        component.PhysicalY = 100;
        _canvas.AddComponent(component, "TestWG");

        var initialCount = _canvas.Components.Count;

        var result = await _svc.CopyComponentAsync("source_1", 500, 500);

        result.ShouldContain("source_1");
        _canvas.Components.Count.ShouldBe(initialCount + 1);
    }

    [Fact]
    public async Task CopyComponentAsync_ValidComponent_ReturnsNewComponentId()
    {
        var component = TestComponentFactory.CreateBasicComponent();
        component.Identifier = "wg_1";
        component.PhysicalX = 0;
        component.PhysicalY = 0;
        _canvas.AddComponent(component, "Straight");

        var result = await _svc.CopyComponentAsync("wg_1", 300, 300);

        result.ShouldContain("New ID:");
        result.ShouldNotContain("Failed");
    }

    [Fact]
    public async Task CopyComponentAsync_ValidComponent_CopyHasDifferentIdentifier()
    {
        var component = TestComponentFactory.CreateBasicComponent();
        component.Identifier = "wg_source";
        _canvas.AddComponent(component, "Straight");

        await _svc.CopyComponentAsync("wg_source", 400, 400);

        var allIds = _canvas.Components.Select(c => c.Component.Identifier).ToList();
        allIds.Distinct().Count().ShouldBe(allIds.Count); // All IDs must be unique
    }

    [Fact]
    public async Task CopyComponentAsync_ValidComponent_ResultMentionsTargetPosition()
    {
        var component = TestComponentFactory.CreateBasicComponent();
        component.Identifier = "comp_1";
        component.PhysicalX = 0;
        component.PhysicalY = 0;
        _canvas.AddComponent(component, "Straight");

        var result = await _svc.CopyComponentAsync("comp_1", 600, 700);

        result.ShouldNotContain("Failed");
        result.ShouldNotContain("not found");
        var copy = _canvas.Components.Skip(1).FirstOrDefault();
        copy.ShouldNotBeNull();
    }

    /// <summary>
    /// Creates a minimal <see cref="LeftPanelViewModel"/> with real but empty dependencies.
    /// No PDKs are loaded so <see cref="LeftPanelViewModel.AllTemplates"/> is empty.
    /// </summary>
    private static LeftPanelViewModel CreateMinimalLeftPanel(DesignCanvasViewModel canvas)
    {
        var libraryManager = new GroupLibraryManager();
        var pdkLoader = new PdkLoader();
        var prefsTempFile = Path.Combine(Path.GetTempPath(), $"cap-test-{Guid.NewGuid()}.json");
        var prefs = new UserPreferencesService(prefsTempFile);
        var hierarchy = new HierarchyPanelViewModel(canvas);
        var pdkManager = new PdkManagerViewModel();
        var componentLibrary = new ComponentLibraryViewModel(libraryManager);

        return new LeftPanelViewModel(
            canvas, libraryManager, pdkLoader, prefs,
            hierarchy, pdkManager, componentLibrary);
    }
}
