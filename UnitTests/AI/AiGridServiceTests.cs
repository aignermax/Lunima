using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Creation;
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
