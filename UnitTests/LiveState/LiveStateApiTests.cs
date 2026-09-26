using System.Text.Json;
using CAP_Core;
using CAP.Avalonia.Services.LiveState;
using CAP.Avalonia.ViewModels;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.LiveState;

/// <summary>
/// Tests for <see cref="LiveStateApi"/>: route dispatch, state snapshots,
/// and command execution against a real MainViewModel.
/// </summary>
public class LiveStateApiTests
{
    private readonly MainViewModel _vm;
    private readonly ErrorConsoleService _errorConsole = new();
    private readonly LiveStateApi _api;

    public LiveStateApiTests()
    {
        _vm = MainViewModelTestHelper.CreateMainViewModel();
        // Direct invoker: tests have no Avalonia dispatcher loop.
        _api = new LiveStateApi(() => _vm, _errorConsole, new LiveStateCommandExecutor(),
            invokeOnUiThread: handler => handler());
    }

    [Fact]
    public async Task GetStatus_ReturnsComponentCounts()
    {
        _vm.Canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());

        var (status, json) = await _api.HandleAsync("GET", "/status", "");

        status.ShouldBe(200);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("componentCount").GetInt32().ShouldBe(1);
        doc.RootElement.GetProperty("app").GetString().ShouldBe("Lunima");
    }

    [Fact]
    public async Task GetDesign_ReturnsComponentWithPositionAndLockState()
    {
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        component.Name = "wg_1";
        component.PhysicalX = 100;
        component.PhysicalY = 50;
        _vm.Canvas.AddComponent(component);

        var (status, json) = await _api.HandleAsync("GET", "/design", "");

        status.ShouldBe(200);
        using var doc = JsonDocument.Parse(json);
        var comp = doc.RootElement.GetProperty("components")[0];
        comp.GetProperty("id").GetString().ShouldBe("wg_1");
        comp.GetProperty("x").GetDouble().ShouldBe(100);
        comp.GetProperty("y").GetDouble().ShouldBe(50);
        comp.GetProperty("isLocked").GetBoolean().ShouldBeFalse();
        comp.GetProperty("isGroup").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task MoveComponent_MovesToAbsolutePosition()
    {
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        component.Name = "wg_move";
        _vm.Canvas.AddComponent(component);

        var (status, json) = await _api.HandleAsync("POST", "/component/move",
            "{\"componentId\": \"wg_move\", \"x\": 320.0, \"y\": 80.0}");

        status.ShouldBe(200);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().ShouldBeTrue();
        var moved = _vm.Canvas.Components.Single(c => c.Component.Identifier == "wg_move");
        moved.X.ShouldBe(320.0, 0.001);
        moved.Y.ShouldBe(80.0, 0.001);
    }

    [Fact]
    public async Task MoveComponent_UnknownId_ReportsFailure()
    {
        var (status, json) = await _api.HandleAsync("POST", "/component/move",
            "{\"componentId\": \"nope\", \"x\": 1, \"y\": 2}");

        status.ShouldBe(200);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().ShouldBeFalse();
        doc.RootElement.GetProperty("message").GetString()!.ShouldContain("not found");
    }

    [Fact]
    public async Task MoveComponent_MissingFields_Returns400()
    {
        var (status, _) = await _api.HandleAsync("POST", "/component/move", "{\"x\": 1}");
        status.ShouldBe(400);
    }

    [Fact]
    public async Task MoveComponent_InvalidJson_Returns400()
    {
        var (status, _) = await _api.HandleAsync("POST", "/component/move", "not json");
        status.ShouldBe(400);
    }

    [Fact]
    public async Task GetErrors_ReturnsLoggedEntries()
    {
        _errorConsole.LogError("routing exploded");

        var (status, json) = await _api.HandleAsync("GET", "/errors", "");

        status.ShouldBe(200);
        using var doc = JsonDocument.Parse(json);
        var entry = doc.RootElement.EnumerateArray().Single();
        entry.GetProperty("level").GetString().ShouldBe("Error");
        entry.GetProperty("message").GetString().ShouldBe("routing exploded");
    }

    [Fact]
    public async Task GetSimulation_BeforeAnyRun_ReportsNoResult()
    {
        var (status, json) = await _api.HandleAsync("GET", "/simulation", "");

        status.ShouldBe(200);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("hasResult").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task RunSimulation_EmptyCanvas_ReportsFailureReason()
    {
        var (status, json) = await _api.HandleAsync("POST", "/simulation/run", "");

        status.ShouldBe(200);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("hasResult").GetBoolean().ShouldBeTrue();
        doc.RootElement.GetProperty("success").GetBoolean().ShouldBeFalse();
        doc.RootElement.GetProperty("errorMessage").GetString()!.ShouldContain("No components");
    }

    [Fact]
    public async Task LoadDesign_MissingFile_ReportsFailure()
    {
        var (status, json) = await _api.HandleAsync("POST", "/design/load",
            "{\"path\": \"/nonexistent/design.lun\"}");

        status.ShouldBe(200);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().ShouldBeFalse();
        doc.RootElement.GetProperty("message").GetString()!.ShouldContain("not found");
    }

    [Fact]
    public async Task UnknownRoute_Returns404WithRouteList()
    {
        var (status, json) = await _api.HandleAsync("GET", "/nope", "");

        status.ShouldBe(404);
        json.ShouldContain("/design");
    }
}
