using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Headless;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.AgentBridge.Headless;

/// <summary>
/// Hierarchical composition the way an agent builds a CPU: the shipped full adder (32 gate
/// groups) becomes one library cell, two instances are placed, and their signals renamed so
/// they form one logic network.
/// </summary>
public class AgentCompositionTests
{
    private static async Task<JsonElement> Ok(HeadlessAgentSession session, string tool, object args)
    {
        var result = await session.Catalog.InvokeAsync(tool, JsonSerializer.SerializeToElement(args));
        result.IsError.ShouldBeFalse(result.Content[0].Text);
        return JsonDocument.Parse(result.Content[0].Text!).RootElement.Clone();
    }

    private static async Task<(HeadlessAgentSession Session, string Library)> FullAdderCell()
    {
        var library = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "lunima-fa-" + System.Guid.NewGuid().ToString("N"))).FullName;
        var session = HeadlessAgentSession.Create(TextWriter.Null, s => s.AddSingleton(new GroupLibraryManager(library)));
        var examples = new ExampleDesignsService(new FileExampleCatalog()).FindExamplesDirectory()!;
        await session.OpenDesignAsync(Path.Combine(examples, "Logic Gate Full Adder.lun"));
        var ids = session.Main.Canvas.Components.Select(c => c.Component.Identifier).ToArray();
        await Ok(session, "create_group", new { component_ids = ids, name = "FA" });
        return (session, library);
    }

    [AvaloniaFact]
    public async Task GroupingGates_FreezesTheirWires_AndExposesOnlyFreePins()
    {
        var (session, library) = await FullAdderCell();
        try
        {
            await using (session)
            {
                var cell = (ComponentGroup)session.Main.Canvas.Components.Single().Component;

                cell.InternalPaths.Count.ShouldBe(36, "every wire between the gates moves into the cell");
                session.Main.Canvas.Connections.ShouldBeEmpty("no wire is left dangling into the cell");
                var network = await Ok(session, "logic_build", new { });
                network.GetProperty("inputs").EnumerateArray().Select(i => i.GetString()).ShouldBe(new[] { "A", "B", "Cin" });
                var pins = (await Ok(session, "get_component", new { id = "FA" })).GetProperty("pin_count").GetInt32();
                pins.ShouldBeLessThan(80, "wired gate pins are internal, not exposed");
            }
        }
        finally { Directory.Delete(library, recursive: true); }
    }

    [AvaloniaFact]
    public async Task TwoLibraryInstances_WithRenamedSignals_FormOneNetwork()
    {
        var (session, library) = await FullAdderCell();
        try
        {
            await using (session)
            {
                await Ok(session, "save_as_prefab", new { group_id = "FA", prefab_name = "FA_CELL" });
                await Ok(session, "new_design", new { });
                await Ok(session, "set_chip_size", new { width_um = 28000, height_um = 4000 });
                await Ok(session, "place_component", new { type = "FA_CELL", x = 200, y = 200, name = "FA0" });
                await Ok(session, "place_component", new { type = "FA_CELL", x = 14500, y = 200, name = "FA1" });

                await Ok(session, "rename_signals", new { id = "FA0", renames = new { A = "A0", B = "B0", S = "S0", Cout = "C1", Cin = "Cin0" } });
                await Ok(session, "rename_signals", new { id = "FA1", renames = new { A = "A1", B = "B1", S = "S1", Cout = "C2", Cin = "Cin1" } });
                var network = await Ok(session, "logic_build", new { });

                network.GetProperty("primary_outputs").EnumerateArray().Select(o => o.GetString()).ShouldBe(new[] { "S0", "C1", "S1", "C2" });
                var rows = (await Ok(session, "logic_truth_table", new { @fixed = new { Cin0 = 0, Cin1 = 0 } }))
                    .GetProperty("rows").EnumerateArray().Select(r => r.GetString()).ToList();
                rows.ShouldContain("1100 -> 0100", "FA0: 1 + 1 = 10");
                rows.ShouldContain("0011 -> 0001", "FA1: 1 + 1 = 10");

                await Ok(session, "undo", new { });
                await Ok(session, "undo", new { });
                var reverted = await session.Catalog.InvokeAsync("logic_build", JsonSerializer.SerializeToElement(new { }));
                reverted.IsError.ShouldBeTrue("undoing both renames brings back the clash of two outputs named S");
            }
        }
        finally { Directory.Delete(library, recursive: true); }
    }
}
