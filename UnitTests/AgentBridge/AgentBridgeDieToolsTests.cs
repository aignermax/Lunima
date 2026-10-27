using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Tools.Dies;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using CAP_Core.Components.Process;
using Shouldly;
using Xunit;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.AgentBridge;

/// <summary>
/// The die tools an external agent drives the assembly with: list, create, set process/PDKs,
/// move and link — each on the shared undo history.
/// </summary>
public class AgentBridgeDieToolsTests
{
    private static readonly ProcessGroup SiN = new("SiN 300", new ProcessFingerprint("SiN", 300, "SiO2", 1550, "SiN 300"), new[] { "sin-a", "sin-b" });

    private readonly DesignCanvasViewModel _canvas = new();
    private readonly CommandManager _commands = new();
    private readonly AgentDesignSession _session;

    public AgentBridgeDieToolsTests()
    {
        _canvas.SetRootChipSize(2000, 2000);
        _session = new AgentDesignSession(_canvas, _commands, () => null, new ExampleDesignsService(new CAP.Avalonia.Services.FileExampleCatalog()),
            processes: () => new[] { SiN });
    }

    [Fact]
    public async Task CreateDie_TurnsTheChipIntoAnAssembly_AndListDiesReportsIt()
    {
        var created = Body(await new CreateDieTool(_session).ExecuteAsync(Args("""{"process":"sin-a","width":1000,"height":800}""")));

        created.GetProperty("converted_chip").GetString().ShouldBe("Die 1");
        created.GetProperty("created").GetProperty("process").GetString().ShouldBe("SiN 300");
        var list = Body(await new ListDiesTool(_session).ExecuteAsync(Args("{}")));
        list.GetProperty("is_assembly").GetBoolean().ShouldBeTrue();
        list.GetProperty("dies").GetArrayLength().ShouldBe(2);
        _commands.Undo();
        _canvas.Dies.IsAssemblyRoot.ShouldBeFalse();
    }

    [Fact]
    public async Task CreateDie_UnknownProcess_IsRefused()
    {
        var result = await new CreateDieTool(_session).ExecuteAsync(Args("""{"process":"InP"}"""));

        result.IsError.ShouldBeTrue();
        result.Content[0].Text!.ShouldContain("SiN 300");
    }

    [Fact]
    public async Task SetDieProcess_SetsProcessAndPdkSubset_Undoably()
    {
        await new CreateDieTool(_session).ExecuteAsync(Args("""{"width":1000,"height":800,"name":"Logic"}"""));

        Body(await new SetDieProcessTool(_session).ExecuteAsync(Args("""{"die":"logic","process":"SiN 300","pdks":["sin-b"]}""")));

        var die = _canvas.Dies.RootDies.Single(d => d.GroupName == "Logic");
        die.ProcessBinding!.DisplayName.ShouldBe("SiN 300");
        die.Die!.EnabledPdkNames.ShouldBe(new[] { "sin-b" });
        (await new SetDieProcessTool(_session).ExecuteAsync(Args("""{"die":"Logic","pdks":["other"]}"""))).IsError.ShouldBeTrue();
        _commands.Undo();
        die.ProcessBinding.ShouldBeNull();
    }

    [Fact]
    public async Task MoveDie_RefusesOverlap()
    {
        await new CreateDieTool(_session).ExecuteAsync(Args("""{"width":1000,"height":800,"name":"B"}"""));

        (await new MoveDieTool(_session).ExecuteAsync(Args("""{"die":"B","x":1500,"y":0}"""))).IsError.ShouldBeTrue();
        Body(await new MoveDieTool(_session).ExecuteAsync(Args("""{"die":"B","dx":300}""")));
        DieAssembly.OutlineOf(_canvas.Dies.RootDies.Single(d => d.GroupName == "B")).X.ShouldBe(2800);
    }

    [Fact]
    public async Task LinkDies_ButtCouplesFacingCouplers_AndReportsTheLoss()
    {
        var left = _canvas.AddComponent(Die("L", 0, 0, 1000, 600, null, FacingEast("l1", 200))).Component as ComponentGroup;
        var right = _canvas.AddComponent(Die("R", 1400, 0, 1000, 600, null, EdgeCoupler("r1", 1400, 260))).Component as ComponentGroup;
        _canvas.RefreshDieInterfaces();
        _canvas.Dies.ApplyScopeBounds();

        var body = Body(await new LinkDiesTool(_session).ExecuteAsync(Args("""{"die":"R","to":"L"}""")));

        var link = body.GetProperty("links").EnumerateArray().Single();
        link.GetProperty("gap_micrometers").GetDouble().ShouldBe(2.0, 1e-6);
        link.GetProperty("lateral_offset_micrometers").GetDouble().ShouldBe(0, 1e-6);
        link.GetProperty("loss_db").GetDouble().ShouldBeGreaterThan(0);
        _canvas.ConnectionManager.Connections.Single().IsCrossChipletFacetLink.ShouldBeTrue();
        right!.ShouldNotBe(left);
    }

    private static Component FacingEast(string id, double y)
    {
        var coupler = EdgeCoupler(id, 1000 - CouplerWidth, y);
        DieEdgeSnapper.Apply(coupler, DieEdgeSnapper.Plan(coupler, new DieOutline(0, 0, 1000, 600), 50)!);
        return coupler;
    }

    private static JsonElement Args(string json) => AgentBridgeTestStack.Parse(json);

    private static JsonElement Body(AgentToolResult result)
    {
        result.IsError.ShouldBeFalse(result.Content[0].Text);
        return AgentBridgeTestStack.Parse(result.Content[0].Text!);
    }
}
