using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.LightCalculation;
using Moq;
using Shouldly;

namespace UnitTests.AI;

/// <summary>
/// Unit tests for <see cref="AiGridService"/>.
/// Uses a real <see cref="DesignCanvasViewModel"/> and real <see cref="CommandManager"/>,
/// but mocks <see cref="SimulationService"/> to avoid heavy core dependencies.
/// </summary>
public class AiGridServiceTests
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CommandManager _commandManager;
    private readonly Mock<SimulationService> _mockSimulation;

    /// <summary>
    /// Builds a test service with the given template list.
    /// </summary>
    private AiGridService CreateService(IReadOnlyList<ComponentTemplate>? templates = null)
    {
        var templateList = templates ?? TestPdkLoader.LoadAllTemplates();
        return new AiGridService(
            _canvas,
            () => templateList,
            _commandManager,
            _mockSimulation.Object);
    }

    public AiGridServiceTests()
    {
        _canvas = new DesignCanvasViewModel();
        _commandManager = new CommandManager();
        _mockSimulation = new Mock<SimulationService>();
    }

    // ── GetGridState ──────────────────────────────────────────────────────────

    [Fact]
    public void GetGridState_EmptyCanvas_ReturnsZeroCounts()
    {
        var service = CreateService();

        var json = service.GetGridState();

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("component_count").GetInt32().ShouldBe(0);
        doc.RootElement.GetProperty("connection_count").GetInt32().ShouldBe(0);
        doc.RootElement.GetProperty("simulation_active").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void GetGridState_ReturnsValidJson()
    {
        var service = CreateService();

        var json = service.GetGridState();

        Should.NotThrow(() => JsonDocument.Parse(json));
    }

    // ── GetAvailableComponentTypes ────────────────────────────────────────────

    [Fact]
    public void GetAvailableComponentTypes_NoTemplates_ReturnsEmptyList()
    {
        var service = CreateService(new List<ComponentTemplate>());

        var types = service.GetAvailableComponentTypes();

        types.ShouldBeEmpty();
    }

    [Fact]
    public void GetAvailableComponentTypes_WithTemplates_ReturnsDistinctSortedNames()
    {
        var templates = new List<ComponentTemplate>
        {
            MakeTemplate("Waveguide", 100, 20),
            MakeTemplate("MMI 1x2", 80, 30),
            MakeTemplate("Waveguide", 100, 20) // duplicate
        };
        var service = CreateService(templates);

        var types = service.GetAvailableComponentTypes();

        types.Count.ShouldBe(2);
        types.ShouldBeInOrder(); // Sorted alphabetically
    }

    // ── PlaceComponent ────────────────────────────────────────────────────────

    [Fact]
    public void PlaceComponent_UnknownType_ReturnsError()
    {
        var service = CreateService(new List<ComponentTemplate>());

        var result = service.PlaceComponent("NonExistentComponent", 100, 100);

        result.ShouldStartWith("Error:");
    }

    [Fact]
    public void PlaceComponent_KnownType_ReturnsSuccessWithComponentId()
    {
        var template = MakeTemplate("Test Waveguide", 100, 20);
        var service = CreateService(new List<ComponentTemplate> { template });

        var result = service.PlaceComponent("Test Waveguide", 500, 500);

        result.ShouldNotStartWith("Error:");
        result.ShouldContain("Component ID:");
        _canvas.Components.Count.ShouldBe(1);
    }

    [Fact]
    public void PlaceComponent_KnownType_CaseInsensitive()
    {
        var template = MakeTemplate("Grating Coupler", 50, 50);
        var service = CreateService(new List<ComponentTemplate> { template });

        var result = service.PlaceComponent("grating coupler", 200, 200);

        result.ShouldNotStartWith("Error:");
    }

    [Fact]
    public void PlaceComponent_AddsComponentToCanvas()
    {
        var template = MakeTemplate("My Component", 50, 50);
        var service = CreateService(new List<ComponentTemplate> { template });

        _canvas.Components.Count.ShouldBe(0);
        service.PlaceComponent("My Component", 300, 300);
        _canvas.Components.Count.ShouldBe(1);
    }

    // ── ClearGrid ─────────────────────────────────────────────────────────────

    [Fact]
    public void ClearGrid_EmptyCanvas_ReturnsZeroCleared()
    {
        var service = CreateService();

        var result = service.ClearGrid();

        result.ShouldContain("0");
    }

    [Fact]
    public void ClearGrid_WithComponents_RemovesAll()
    {
        var template = MakeTemplate("WG", 50, 20);
        var service = CreateService(new List<ComponentTemplate> { template });
        service.PlaceComponent("WG", 200, 200);
        service.PlaceComponent("WG", 400, 400);
        _canvas.Components.Count.ShouldBe(2);

        service.ClearGrid();

        _canvas.Components.Count.ShouldBe(0);
    }

    // ── GetLightValues ────────────────────────────────────────────────────────

    [Fact]
    public void GetLightValues_SimulationNotActive_ReturnsNoDataMessage()
    {
        var service = CreateService();

        var result = service.GetLightValues();

        result.ShouldContain("start_simulation");
    }

    // ── StopSimulation ────────────────────────────────────────────────────────

    [Fact]
    public void StopSimulation_HidesPowerFlow()
    {
        var service = CreateService();
        _canvas.ShowPowerFlow = true;

        service.StopSimulation();

        _canvas.ShowPowerFlow.ShouldBeFalse();
    }

    // ── CreateConnection ──────────────────────────────────────────────────────

    [Fact]
    public void CreateConnection_UnknownFromComponent_ReturnsError()
    {
        var service = CreateService();

        var result = service.CreateConnection("NonExistent_1", "NonExistent_2");

        result.ShouldStartWith("Error:");
        result.ShouldContain("NonExistent_1");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a minimal ComponentTemplate with two pins and a zero-transmission S-Matrix.
    /// </summary>
    private static ComponentTemplate MakeTemplate(string name, double width, double height)
    {
        return new ComponentTemplate
        {
            Name = name,
            PdkSource = "Test PDK",
            WidthMicrometers = width,
            HeightMicrometers = height,
            PinDefinitions = new[]
            {
                new PinDefinition("left", 0, height / 2, 180),
                new PinDefinition("right", width, height / 2, 0)
            },
            CreateSMatrix = pins =>
            {
                var allIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
                return new SMatrix(allIds, new());
            }
        };
    }
}
