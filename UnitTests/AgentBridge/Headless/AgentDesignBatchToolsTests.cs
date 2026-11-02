using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CAP.Avalonia.Services.AgentBridge.Headless;
using Shouldly;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.AgentBridge.Headless;

/// <summary>
/// Whole-design authoring: port-relative placement, bundles, length matching, atomic batches
/// and declarative designs (Lunima JSON and gdsfactory YAML round trip).
/// </summary>
public class AgentDesignBatchToolsTests
{
    private static async Task<JsonElement> Ok(HeadlessAgentSession session, string tool, string args = "{}")
    {
        var result = await session.Catalog.InvokeAsync(tool, JsonDocument.Parse(args).RootElement.Clone());
        result.IsError.ShouldBeFalse(result.Content[0].Text);
        return JsonDocument.Parse(result.Content[0].Text!).RootElement.Clone();
    }

    private static async Task<HeadlessAgentSession> Empty()
    {
        var session = HeadlessAgentSession.Create(TextWriter.Null);
        await Ok(session, "new_design");
        return session;
    }

    [AvaloniaFact]
    public async Task AttachTo_TurnsTheNewPartToFaceThePin_AndWiresIt()
    {
        await using var session = await Empty();
        await Ok(session, "place_component", """{"type":"Grating Coupler","pdk":"Demo PDK","x":100,"y":500,"name":"in"}""");
        await Ok(session, "place_component", """{"type":"1x2 MMI Splitter","x":400,"y":400,"name":"split"}""");

        var placed = await Ok(session, "place_component",
            """{"type":"Grating Coupler","pdk":"Demo PDK","attach_to":"split.out1","pin":"waveguide","gap_um":80,"name":"out"}""");

        var part = placed.GetProperty("placed");
        part.GetProperty("rotation_deg").GetDouble().ShouldBe(180, "the coupler turns round to face the splitter output");
        var pin = part.GetProperty("pins").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "waveguide");
        var target = (await Ok(session, "get_component", """{"id":"split"}""")).GetProperty("pins").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "out1");
        (pin.GetProperty("x").GetDouble() - target.GetProperty("x").GetDouble()).ShouldBe(80, 0.01);
        pin.GetProperty("y").GetDouble().ShouldBe(target.GetProperty("y").GetDouble(), 0.01);
        pin.GetProperty("connected_to").GetProperty("pin").GetString().ShouldEndWith(".out1");
    }

    [AvaloniaFact]
    public async Task Bundle_RoutesTogether_AndMatchLengthsRemovesTheSkew()
    {
        await using var session = await Empty();
        await Ok(session, "place_component", """{"type":"1x2 MMI Splitter","x":0,"y":400,"name":"a"}""");
        await Ok(session, "place_component", """{"type":"1x2 MMI Splitter","x":500,"y":100,"name":"b"}""");
        await Ok(session, "place_component", """{"type":"1x2 MMI Splitter","x":500,"y":800,"name":"c"}""");

        var bundle = await Ok(session, "connect_bundle",
            """{"connections":[{"from":"a.out1","to":"b.in"},{"from":"a.out2","to":"c.in"}]}""");
        bundle.GetProperty("blocked").GetInt32().ShouldBe(0);
        bundle.GetProperty("skew_um").GetDouble().ShouldBeGreaterThan(1);

        var matched = await Ok(session, "match_lengths", """{"connection_ids":["a.out1","a.out2"]}""");
        matched.GetProperty("skew_um").GetDouble().ShouldBeLessThan(0.5);
        await Ok(session, "undo");
        (await Ok(session, "list_connections")).GetProperty("connections").EnumerateArray()
            .Select(c => c.GetProperty("length_um").GetDouble()).Distinct().Count().ShouldBe(2, "undo restores the unmatched routes");
    }

    [AvaloniaFact]
    public async Task Batch_IsAtomic()
    {
        await using var session = await Empty();

        var result = await session.Catalog.InvokeAsync("batch", JsonDocument.Parse("""
            {"steps":[{"tool":"place_component","arguments":{"type":"1x2 MMI Splitter","x":0,"y":0}},
                      {"tool":"place_component","arguments":{"type":"Nope","x":0,"y":0}}]}
            """).RootElement.Clone());

        result.IsError.ShouldBeTrue();
        var body = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        body.GetProperty("rolled_back").GetBoolean().ShouldBeTrue();
        session.Main.Canvas.Components.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task ApplyDesign_JsonWithParametersAndSources_Simulates()
    {
        await using var session = await Empty();

        await Ok(session, "apply_design", """
            {"design":{"instances":{
              "in":{"type":"Grating Coupler","pdk":"Demo PDK","x":100,"y":800,"light_source":{"enabled":true,"wavelength_nm":1550}},
              "split":{"type":"1x2 MMI Splitter","attach_to":"in.waveguide","pin":"in","parameters":{"splitting_ratio":70}},
              "top":{"type":"Grating Coupler","pdk":"Demo PDK","x":900,"y":500,"rotation_deg":180,"light_source":{"enabled":false}},
              "bot":{"type":"Grating Coupler","pdk":"Demo PDK","x":900,"y":1100,"rotation_deg":180,"light_source":{"enabled":false}}},
             "connections":[{"from":"split.out1","to":"top.waveguide"},{"from":"split.out2","to":"bot.waveguide"}]}}
            """);

        var outputs = (await Ok(session, "simulate")).GetProperty("outputs").EnumerateArray()
            .ToDictionary(o => o.GetProperty("name").GetString()!, o => o.GetProperty("power").GetDouble());
        (outputs["top"] / outputs["bot"]).ShouldBe(70.0 / 30.0, 0.05, "the 70 % splitting ratio reaches the outputs");
    }

    [AvaloniaFact]
    public async Task ApplyDesign_GdsFactoryYaml_RoundTripsTheExport()
    {
        await using var session = await Empty();
        await Ok(session, "apply_design", """
            {"design":{"instances":{
              "in":{"type":"Grating Coupler","pdk":"Demo PDK","x":0,"y":200},
              "split":{"type":"1x2 MMI Splitter","x":200,"y":180},
              "out":{"type":"Grating Coupler","pdk":"Demo PDK","x":600,"y":120,"rotation_deg":180}},
             "connections":[{"from":"in.waveguide","to":"split.in"},{"from":"split.out1","to":"out.waveguide"}]}}
            """);
        var yaml = (await session.Catalog.InvokeAsync("export_netlist",
            JsonDocument.Parse("""{"format":"gdsfactory_yaml"}""").RootElement.Clone())).Content[0].Text!;
        var before = Poses(await Ok(session, "list_components"));

        await Ok(session, "apply_design", JsonSerializer.Serialize(new { text = yaml, clear = true }));

        Poses(await Ok(session, "list_components")).ShouldBe(before, ignoreOrder: true);
        (await Ok(session, "list_connections")).GetProperty("count").GetInt32().ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task ApplyDesign_Failure_UndoesEverything()
    {
        await using var session = await Empty();

        var result = await session.Catalog.InvokeAsync("apply_design", JsonDocument.Parse("""
            {"design":{"instances":{"a":{"type":"1x2 MMI Splitter","x":0,"y":0},"b":{"type":"Nope","x":400,"y":0}}}}
            """).RootElement.Clone());

        result.IsError.ShouldBeTrue();
        result.Content[0].Text.ShouldContain("'b'");
        session.Main.Canvas.Components.ShouldBeEmpty();
    }

    private static string[] Poses(JsonElement components) =>
        components.GetProperty("components").EnumerateArray()
            .Select(c => $"{c.GetProperty("x").GetDouble():F2},{c.GetProperty("y").GetDouble():F2},{c.GetProperty("rotation").GetDouble()}")
            .ToArray();
}
