using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Tools.Edit;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;
using CAP.Avalonia.Services.AgentBridge.Tools.Session;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.AgentBridge;

/// <summary>
/// The agent workflow on a real shipped example loaded through the production load path:
/// orient, find blocked connections, move a component, undo. Full routing passes on a
/// multi-gate design make these heavyweight.
/// </summary>
[Trait("Category", "Slow")]
public class AgentBridgeExampleDesignTests
{
    private const string ExampleFileName = "Logic Gate Counter 2-bit.lun";

    private static async Task<(AgentDesignSession Session, CommandManager Commands)> LoadAsync()
    {
        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(ExampleFileName);
        await fileOps.PostLoadRouting;
        var commands = new CommandManager();
        return (new AgentDesignSession(canvas, commands, () => fileOps, new ExampleDesignsService()), commands);
    }

    private static JsonElement Body(AgentToolResult result)
    {
        result.IsError.ShouldBeFalse(result.Content[0].Text);
        return JsonDocument.Parse(result.Content[0].Text!).RootElement.Clone();
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task Summary_AndBlockedList_AgreeWithTheCanvas()
    {
        var (session, _) = await LoadAsync();

        var summary = Body(await new GetDesignSummaryTool(session).ExecuteAsync(Args("{}")));
        var blocked = Body(await new ListConnectionsTool(session).ExecuteAsync(Args("""{"blocked_only":true}""")));

        summary.GetProperty("component_count").GetInt32().ShouldBe(session.Canvas.Components.Count);
        summary.GetProperty("connection_count").GetInt32().ShouldBe(session.Canvas.Connections.Count);
        var expectedBlocked = session.Canvas.Connections.Count(c => c.Connection.IsBlockedFallback);
        summary.GetProperty("blocked_count").GetInt32().ShouldBe(expectedBlocked);
        blocked.GetProperty("count").GetInt32().ShouldBe(expectedBlocked);
        // The example ships chain crossings, which carry no crossing record.
        summary.GetProperty("crossing_piece_count").GetInt32().ShouldBeGreaterThan(0);
        foreach (var connection in blocked.GetProperty("connections").EnumerateArray())
        {
            connection.GetProperty("blocked").GetBoolean().ShouldBeTrue();
            connection.GetProperty("failure_reason").GetString().ShouldNotBeNullOrEmpty();
        }
    }

    [Fact]
    [Trait("Category", "Heavy")]
    public async Task MoveGroup_ThenUndo_RestoresPositionAndConnectionCount()
    {
        var (session, commands) = await LoadAsync();
        var target = session.Canvas.Components.First(c => !c.Component.IsLocked);
        var (x, y) = (target.X, target.Y);
        var connections = session.Canvas.Connections.Count;
        var id = target.Component.Identifier;

        var moved = Body(await new MoveComponentTool(session).ExecuteAsync(
            Args($$"""{"id":"{{id}}","dx":0,"dy":-50,"force":true}""")));
        moved.GetProperty("routing_complete").GetBoolean().ShouldBeTrue();
        target.Y.ShouldBe(y - 50, 0.001);
        commands.UndoCount.ShouldBe(1);

        Body(await new UndoTool(session).ExecuteAsync(Args("{}")));
        target.X.ShouldBe(x, 0.001);
        target.Y.ShouldBe(y, 0.001);
        session.Canvas.Connections.Count.ShouldBe(connections);
    }

    [Fact]
    public async Task LoadExample_ByNameWithoutExtension_ReplacesTheDesign()
    {
        var (session, _) = await LoadAsync();

        var body = Body(await new LoadExampleTool(session).ExecuteAsync(Args("""{"name":"Logic Gate Half Adder"}""")));

        body.GetProperty("loaded").GetString().ShouldBe("Logic Gate Half Adder.lun");
        session.Canvas.Components.Count.ShouldBe(body.GetProperty("component_count").GetInt32());
    }

    [Fact]
    public async Task LoadExample_WithoutName_ListsExamples()
    {
        var (session, _) = await LoadAsync();

        var body = Body(await new LoadExampleTool(session).ExecuteAsync(Args("{}")));

        body.GetProperty("examples").EnumerateArray().Select(e => e.GetString()).ShouldContain(ExampleFileName);
    }
}
