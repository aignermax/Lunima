using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Headless;
using CAP_Core.Components.Creation;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.AgentBridge.Headless;

/// <summary>
/// The path from optics to a processor through agent tools: grouping, gate definition with an
/// extracted optical truth table, logic networks with registers, and the ISA playground.
/// </summary>
public class AgentLogicToolsTests
{
    private static async Task<HeadlessAgentSession> Open(string example)
    {
        var session = HeadlessAgentSession.Create(TextWriter.Null);
        var examples = new ExampleDesignsService(new FileExampleCatalog()).FindExamplesDirectory()!;
        await session.OpenDesignAsync(Path.Combine(examples, example));
        return session;
    }

    private static async Task<JsonElement> Ok(HeadlessAgentSession session, string tool, string args = "{}")
    {
        var result = await session.Catalog.InvokeAsync(tool, JsonDocument.Parse(args).RootElement.Clone());
        result.IsError.ShouldBeFalse(result.Content[0].Text);
        return JsonDocument.Parse(result.Content[0].Text!).RootElement.Clone();
    }

    private static async Task<string> Fails(HeadlessAgentSession session, string tool, string args = "{}")
    {
        var result = await session.Catalog.InvokeAsync(tool, JsonDocument.Parse(args).RootElement.Clone());
        result.IsError.ShouldBeTrue();
        return result.Content[0].Text!;
    }

    [AvaloniaFact]
    public async Task CreateGroup_AndUngroup_AreUndoable()
    {
        await using var session = HeadlessAgentSession.Create(TextWriter.Null);
        await Ok(session, "new_design");
        await Ok(session, "place_component", """{"type":"1x2 MMI Splitter","x":0,"y":0,"name":"a"}""");
        await Ok(session, "place_component", """{"type":"1x2 MMI Splitter","x":300,"y":0,"name":"b"}""");

        var grouped = await Ok(session, "create_group", """{"component_ids":["a","b"],"name":"pair"}""");

        grouped.GetProperty("group").GetProperty("child_count").GetInt32().ShouldBe(2);
        session.Main.Canvas.Components.ShouldHaveSingleItem();
        await Ok(session, "ungroup", """{"id":"pair"}""");
        session.Main.Canvas.Components.Count.ShouldBe(2);
        await Ok(session, "undo");
        session.Main.Canvas.Components.ShouldHaveSingleItem("undo restores the group");
    }

    [AvaloniaFact]
    public async Task MakeGate_ExtractsTheOpticalTruthTable_WithAnAutomaticThreshold()
    {
        await using var session = await Open("Logic Gate NOT-NAND.lun");

        var gate = await Ok(session, "make_gate",
            """{"id":"NOT/NAND Gate","inputs":["A","B"],"outputs":["Y"],"bias":["BIAS"],"name":"NAND2"}""");

        gate.GetProperty("noise_margin_of_gap").GetDouble().ShouldBeGreaterThan(0.05);
        var rows = gate.GetProperty("truth_table").EnumerateArray()
            .Select(r => (r.GetProperty("inputs").GetProperty("A").GetInt32(), r.GetProperty("inputs").GetProperty("B").GetInt32(),
                r.GetProperty("outputs").GetProperty("Y").GetInt32()))
            .ToList();
        rows.ShouldBe(new[] { (0, 0, 1), (1, 0, 1), (0, 1, 1), (1, 1, 0) }, ignoreOrder: true);

        var table = await Ok(session, "logic_build");
        table.GetProperty("gates")[0].GetProperty("id").GetString().ShouldBe("NAND2");
        (await Ok(session, "logic_truth_table")).GetProperty("rows").EnumerateArray().Select(r => r.GetString())
            .ShouldBe(new[] { "00 -> 1", "10 -> 1", "01 -> 1", "11 -> 0" });
    }

    [AvaloniaFact]
    public async Task MakeGate_RejectsUnknownPinsAndTooManyInputs()
    {
        await using var session = await Open("Logic Gate NOT-NAND.lun");

        (await Fails(session, "make_gate", """{"id":"NOT/NAND Gate","inputs":["Q"],"outputs":["Y"]}""")).ShouldContain("BIAS");
        (await Fails(session, "make_gate", """{"id":"NOT/NAND Gate","inputs":["A","A","B","Y","BIAS"],"outputs":["Y"]}"""))
            .ShouldContain("at most");
    }

    [AvaloniaFact]
    public async Task LogicStep_LoadsAndHoldsARegister()
    {
        await using var session = await Open("Logic Gate Register 2-bit.lun");
        (await Fails(session, "logic_evaluate")).ShouldContain("logic_build");
        await Ok(session, "logic_build");

        var loaded = await Ok(session, "logic_step", """{"inputs":{"LOAD":1,"D0":1,"D1":0},"reset":true}""");
        var held = await Ok(session, "logic_step", """{"inputs":{"LOAD":0,"D0":0,"D1":1}}""");

        loaded.GetProperty("registers").GetProperty("REG0.Y").GetInt32().ShouldBe(1);
        loaded.GetProperty("registers").GetProperty("REG1.Y").GetInt32().ShouldBe(0);
        held.GetProperty("registers").GetProperty("REG0.Y").GetInt32().ShouldBe(1, "LOAD=0 holds the stored word");
        held.GetProperty("registers").GetProperty("REG1.Y").GetInt32().ShouldBe(0);
        (await Fails(session, "logic_evaluate", """{"inputs":{"X":1}}""")).ShouldContain("Unknown input");
    }

    [AvaloniaFact]
    public async Task IsaRun_GoldenModel_RunsTheSamplesToHalt()
    {
        await using var session = HeadlessAgentSession.Create(TextWriter.Null);

        var samples = await Ok(session, "isa_run");
        samples.GetProperty("samples").GetArrayLength().ShouldBeGreaterThan(0);
        var run = await Ok(session, "isa_run", """{"program":"LOAD 7\nSTORE 0\nLOAD 9\nADD 0\nHALT"}""");

        run.GetProperty("halted").GetBoolean().ShouldBeTrue();
        run.GetProperty("accumulator").GetInt32().ShouldBe(0, "7 + 9 wraps modulo 16");
        (await Fails(session, "isa_run", """{"program":"FLY 3"}""")).ShouldContain("Assembly failed");
        (await Fails(session, "isa_run", """{"program":"HALT","photonic":true}""")).ShouldContain("logic_build");
    }

    [AvaloniaFact]
    public async Task Prefabs_ComposeGatesIntoLargerLogic()
    {
        var library = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "lunima-library-" + System.Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var session = HeadlessAgentSession.Create(TextWriter.Null,
                services => services.AddSingleton(new GroupLibraryManager(library)));
            await using (session)
            {
                var examples = new ExampleDesignsService(new FileExampleCatalog()).FindExamplesDirectory()!;
                await session.OpenDesignAsync(Path.Combine(examples, "Logic Gate NOT-NAND.lun"));
                await Ok(session, "make_gate", """{"id":"NOT/NAND Gate","inputs":["A","B"],"outputs":["Y"],"bias":["BIAS"],"name":"NAND"}""");
                (await Ok(session, "save_as_prefab", """{"group_id":"NAND"}""")).GetProperty("is_gate").GetBoolean().ShouldBeTrue();
                (await Fails(session, "save_as_prefab", """{"group_id":"NAND"}""")).ShouldContain("replace=true");

                await Ok(session, "new_design");
                (await Ok(session, "list_component_types", """{"filter":"NAND"}""")).GetProperty("groups").GetArrayLength().ShouldBe(1);
                await Ok(session, "place_component", """{"type":"NAND","x":0,"y":0,"name":"N1"}""");
                await Ok(session, "place_component", """{"type":"NAND","x":0,"y":400,"name":"N2"}""");
                await Ok(session, "create_connection", """{"from_component":"N1","from_pin":"Y","to_component":"N2","to_pin":"A"}""");
                await Ok(session, "logic_build");

                var table = await Ok(session, "logic_truth_table");
                table.GetProperty("input_order").EnumerateArray().Select(i => i.GetString())
                    .ShouldBe(new[] { "N1.A", "N1.B", "N2.B" });
                // N2.Y = NAND(NAND(A, B), N2.B): with N2.B = 1 it equals A AND B.
                table.GetProperty("rows").EnumerateArray().Select(r => r.GetString())
                    .ShouldContain("111 -> 01");
                table.GetProperty("rows").EnumerateArray().Select(r => r.GetString())
                    .ShouldContain("011 -> 10");
                await Ok(session, "delete_prefab", """{"prefab_name":"NAND"}""");
                (await Fails(session, "delete_prefab", """{"prefab_name":"NAND"}""")).ShouldContain("No library group");
            }
        }
        finally { Directory.Delete(library, recursive: true); }
    }

    [AvaloniaFact]
    [Trait("Category", "Slow")]
    public async Task IsaRun_Photonic_AddsOnTheAdderChip()
    {
        await using var session = await Open("Logic Gate 4-Bit Adder.lun");
        await Ok(session, "logic_build");

        var run = await Ok(session, "isa_run", """{"program":"LOAD 6\nSTORE 0\nLOAD 7\nADD 0\nHALT","photonic":true}""");

        run.GetProperty("accumulator").GetInt32().ShouldBe(13);
        run.GetProperty("photonic_units").GetProperty("add").GetBoolean().ShouldBeTrue();
        run.GetProperty("photonic_status").GetString().ShouldContain("Photonic ADD");
    }
}
