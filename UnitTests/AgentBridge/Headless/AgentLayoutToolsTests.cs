using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Headless;
using Shouldly;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.AgentBridge.Headless;

/// <summary>Floorplanning and chiplet tools: free space, chip size, die removal.</summary>
public class AgentLayoutToolsTests
{
    private static async Task<JsonElement> Ok(HeadlessAgentSession session, string tool, string args = "{}")
    {
        var result = await session.Catalog.InvokeAsync(tool, JsonDocument.Parse(args).RootElement.Clone());
        result.IsError.ShouldBeFalse(result.Content[0].Text);
        return JsonDocument.Parse(result.Content[0].Text!).RootElement.Clone();
    }

    [AvaloniaFact]
    public async Task Floorplan_ReportsUseAndFreeAreas_AndFreeSpaceAvoidsParts()
    {
        await using var session = HeadlessAgentSession.Create(TextWriter.Null);
        await Ok(session, "new_design");
        await Ok(session, "set_chip_size", """{"width_um":2000,"height_um":1000}""");
        await Ok(session, "place_component", """{"type":"Phase Shifter","x":0,"y":0,"name":"ps"}""");

        var plan = await Ok(session, "get_floorplan");
        plan.GetProperty("chip").GetProperty("width").GetDouble().ShouldBe(2000);
        plan.GetProperty("component_count").GetInt32().ShouldBe(1);
        plan.GetProperty("occupied_fraction").GetDouble().ShouldBeInRange(0.001, 0.5);
        var largest = plan.GetProperty("free_areas")[0];
        (largest.GetProperty("width").GetDouble() * largest.GetProperty("height").GetDouble()).ShouldBeGreaterThan(1_000_000);

        var spots = (await Ok(session, "find_free_space", """{"width":500,"height":60,"near_x":0,"near_y":0,"count":2}"""))
            .GetProperty("spots").EnumerateArray().ToList();
        spots.Count.ShouldBe(2);
        foreach (var spot in spots)
        {
            var place = $$"""{"type":"Phase Shifter","x":{{spot.GetProperty("x").GetDouble()}},"y":{{spot.GetProperty("y").GetDouble()}}}""";
            (await Ok(session, "place_component", place)).GetProperty("nudged").GetBoolean()
                .ShouldBeFalse("a reported spot is free exactly as given");
        }
    }

    [AvaloniaFact]
    public async Task FindFreeSpace_RefusesABlockLargerThanTheChip()
    {
        await using var session = HeadlessAgentSession.Create(TextWriter.Null);
        await Ok(session, "new_design");
        await Ok(session, "set_chip_size", """{"width_um":1000,"height_um":1000}""");

        var result = await session.Catalog.InvokeAsync("find_free_space",
            JsonDocument.Parse("""{"width":5000,"height":10}""").RootElement.Clone());

        result.IsError.ShouldBeTrue();
        result.Content[0].Text.ShouldContain("set_chip_size");
    }

    [AvaloniaFact]
    public async Task DeleteDie_RemovesAChiplet_AndUndoRestoresIt()
    {
        await using var session = HeadlessAgentSession.Create(TextWriter.Null);
        var examples = new ExampleDesignsService(new FileExampleCatalog()).FindExamplesDirectory()!;
        await session.OpenDesignAsync(Path.Combine(examples, "Logic Gate Across Two Chiplets.lun"));
        var before = (await Ok(session, "list_dies")).GetProperty("dies").GetArrayLength();

        await Ok(session, "delete_die", """{"die":"Chiplet B"}""");

        (await Ok(session, "list_dies")).GetProperty("dies").GetArrayLength().ShouldBe(before - 1);
        await Ok(session, "undo");
        (await Ok(session, "list_dies")).GetProperty("dies").GetArrayLength().ShouldBe(before);
    }
}
