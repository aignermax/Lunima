using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Tools.Edit;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge;

/// <summary>
/// Inspection and edit tools against a small headless canvas: two waveguides joined by one
/// connection. Edits must land on the shared undo history so the user's Ctrl+Z reverts them.
/// </summary>
public class AgentBridgeEditToolsTests
{
    private const double ChipSize = 3000;
    private const double WaveguideSize = 250;

    private readonly DesignCanvasViewModel _canvas = new();
    private readonly CommandManager _commands = new();
    private readonly AgentDesignSession _session;
    private readonly ComponentViewModel _a;
    private readonly ComponentViewModel _b;

    public AgentBridgeEditToolsTests()
    {
        _canvas.InitializeAStarRouting(0, 0, ChipSize, ChipSize);
        _a = AddWaveguide("wgA", 100, 100);
        _b = AddWaveguide("wgB", 1000, 100);
        _canvas.ConnectPins(_a.Component.PhysicalPins.Single(p => p.Name == "out"),
            _b.Component.PhysicalPins.Single(p => p.Name == "in"));
        _session = new AgentDesignSession(_canvas, _commands, () => null, new ExampleDesignsService(new CAP.Avalonia.Services.FileExampleCatalog()));
    }

    private ComponentViewModel AddWaveguide(string id, double x, double y)
    {
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        component.Identifier = id;
        component.WidthMicrometers = WaveguideSize;
        component.HeightMicrometers = WaveguideSize;
        component.PhysicalX = x;
        component.PhysicalY = y;
        return _canvas.AddComponent(component);
    }

    private static JsonElement Args(string json) => AgentBridgeTestStack.Parse(json);

    private static JsonElement Body(AgentToolResult result)
    {
        result.IsError.ShouldBeFalse(result.Content[0].Text);
        return AgentBridgeTestStack.Parse(result.Content[0].Text!);
    }

    [Fact]
    public async Task ListComponents_ReportsIdsPositionsAndBounds()
    {
        var body = Body(await new ListComponentsTool(_session).ExecuteAsync(Args("{}")));

        body.GetProperty("count").GetInt32().ShouldBe(2);
        var b = body.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("id").GetString() == "wgB");
        b.GetProperty("x").GetDouble().ShouldBe(1000);
        b.GetProperty("bounds").GetProperty("max_x").GetDouble().ShouldBeGreaterThan(1000);
    }

    [Fact]
    public async Task ListConnections_WritesEndpointsAsComponentDotPin()
    {
        var body = Body(await new ListConnectionsTool(_session).ExecuteAsync(Args("{}")));

        var connection = body.GetProperty("connections")[0];
        connection.GetProperty("start").GetString().ShouldBe("wgA.out");
        connection.GetProperty("end").GetString().ShouldBe("wgB.in");
        connection.GetProperty("is_crossing_piece").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task ListConnections_ReportsTheInsertionLossBreakdown()
    {
        await _canvas.RecalculateRoutesAsync();
        var body = Body(await new ListConnectionsTool(_session).ExecuteAsync(Args("{}")));

        var loss = body.GetProperty("connections")[0].GetProperty("loss");
        double total = loss.GetProperty("total_db").GetDouble();
        total.ShouldBeGreaterThan(0);
        total.ShouldBe(loss.GetProperty("propagation_db").GetDouble()
            + loss.GetProperty("bends_db").GetDouble()
            + loss.GetProperty("junctions_db").GetDouble(), 1e-5);
        loss.GetProperty("bend_count").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
        loss.GetProperty("waveguide_family").GetString().ShouldBe("Generic");
    }

    [Fact]
    public async Task ListConnections_BlockedOnly_MatchesSummaryBlockedCount()
    {
        var blocked = Body(await new ListConnectionsTool(_session).ExecuteAsync(Args("""{"blocked_only":true}""")));
        var summary = Body(await new GetDesignSummaryTool(_session).ExecuteAsync(Args("{}")));

        blocked.GetProperty("count").GetInt32().ShouldBe(summary.GetProperty("blocked_count").GetInt32());
        summary.GetProperty("component_count").GetInt32().ShouldBe(2);
        summary.GetProperty("connection_count").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task MoveComponent_ThenUndo_RestoresPosition()
    {
        var result = await new MoveComponentTool(_session).ExecuteAsync(Args("""{"id":"wgB","dx":0,"dy":300}"""));

        Body(result).GetProperty("routing_complete").GetBoolean().ShouldBeTrue();
        _b.Y.ShouldBe(400);
        _commands.UndoCount.ShouldBe(1, "the move is one step on the shared undo history");

        Body(await new UndoTool(_session).ExecuteAsync(Args("{}")));
        _b.Y.ShouldBe(100);
        _b.X.ShouldBe(1000);
    }

    [Fact]
    public async Task MoveComponent_AbsoluteTarget_Moves()
    {
        Body(await new MoveComponentTool(_session).ExecuteAsync(Args("""{"id":"wgB","x":1200,"y":600}""")));

        (_b.X, _b.Y).ShouldBe((1200, 600));
    }

    [Fact]
    public async Task MoveComponent_OntoAnotherComponent_IsRefusedWithoutForce()
    {
        var result = await new MoveComponentTool(_session).ExecuteAsync(Args("""{"id":"wgB","x":100,"y":100}"""));

        result.IsError.ShouldBeTrue();
        _b.X.ShouldBe(1000, "a refused move leaves the component where it was");
        _commands.UndoCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("""{"id":"missing","dx":5}""")]
    [InlineData("""{"id":"wgB"}""")]
    public async Task MoveComponent_BadArguments_AreToolErrors(string json)
    {
        var result = await new MoveComponentTool(_session).ExecuteAsync(Args(json));

        result.IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteConnection_ThenUndo_RestoresIt()
    {
        var id = _canvas.Connections.Single().Connection.Id.ToString();

        Body(await new DeleteConnectionTool(_session).ExecuteAsync(Args($$"""{"id":"{{id}}"}""")));
        _canvas.Connections.ShouldBeEmpty();

        _commands.Undo();
        _canvas.Connections.Count.ShouldBe(1);
    }

    [Fact]
    public async Task DeleteComponent_RemovesItsConnection_AndUndoBringsBothBack()
    {
        var body = Body(await new DeleteComponentTool(_session).ExecuteAsync(Args("""{"id":"wgA"}""")));

        body.GetProperty("connections_removed").GetInt32().ShouldBe(1);
        _canvas.Components.Select(c => c.Component.Identifier).ShouldBe(new[] { "wgB" });

        Body(await new UndoTool(_session).ExecuteAsync(Args("{}")));
        _canvas.Components.Count.ShouldBe(2);
        _canvas.Connections.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RerouteConnections_UnknownId_IsToolError()
    {
        var result = await new RerouteConnectionsTool(_session).ExecuteAsync(Args("""{"ids":["not-a-guid"]}"""));

        result.IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task RerouteConnections_ThenTimingsAreAvailable()
    {
        var id = _canvas.Connections.Single().Connection.Id.ToString();

        var body = Body(await new RerouteConnectionsTool(_session).ExecuteAsync(Args($$"""{"ids":["{{id}}"]}""")));
        body.GetProperty("connections").GetArrayLength().ShouldBe(1);

        var timings = Body(await new GetRoutingTimingsTool(_session).ExecuteAsync(Args("{}")));
        timings.GetProperty("available").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Undo_WithEmptyHistory_IsToolError()
    {
        (await new UndoTool(_session).ExecuteAsync(Args("{}"))).IsError.ShouldBeTrue();
    }
}
