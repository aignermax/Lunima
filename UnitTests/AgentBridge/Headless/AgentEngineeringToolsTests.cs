using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CAP.Avalonia.Services.AgentBridge.Headless;
using Shouldly;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.AgentBridge.Headless;

/// <summary>Component insight, server-side optimisation and fibre-array generation.</summary>
public class AgentEngineeringToolsTests
{
    private const string Mzi = """
        {"clear":true,"design":{"instances":{
          "in":{"type":"Grating Coupler","pdk":"Demo PDK","x":0,"y":200,"light_source":{"enabled":true,"wavelength_nm":1550}},
          "split":{"type":"1x2 MMI Splitter","x":200,"y":180},
          "ps":{"type":"Phase Shifter","x":400,"y":60},
          "comb":{"type":"2x2 MMI Coupler","x":800,"y":170},
          "bar":{"type":"Grating Coupler","pdk":"Demo PDK","x":1200,"y":120,"rotation_deg":180,"light_source":{"enabled":false}},
          "cross":{"type":"Grating Coupler","pdk":"Demo PDK","x":1200,"y":300,"rotation_deg":180,"light_source":{"enabled":false}}},
         "connections":[{"from":"in.waveguide","to":"split.in"},{"from":"split.out1","to":"ps.in"},{"from":"ps.out","to":"comb.in1"},
                        {"from":"split.out2","to":"comb.in2"},{"from":"comb.out1","to":"bar.waveguide"},{"from":"comb.out2","to":"cross.waveguide"}]}}
        """;

    private static async Task<JsonElement> Ok(HeadlessAgentSession session, string tool, string args = "{}")
    {
        var result = await session.Catalog.InvokeAsync(tool, JsonDocument.Parse(args).RootElement.Clone());
        result.IsError.ShouldBeFalse(result.Content[0].Text);
        return JsonDocument.Parse(result.Content[0].Text!).RootElement.Clone();
    }

    [AvaloniaFact]
    public async Task DescribeComponentType_ReadsTheSplittingFromTheModel()
    {
        await using var session = HeadlessAgentSession.Create(TextWriter.Null);

        var mmi = await Ok(session, "describe_component_type", """{"type":"2x2 MMI Coupler"}""");

        mmi.GetProperty("wavelength_nm").GetInt32().ShouldBe(1550);
        var fromIn1 = mmi.GetProperty("transmission").EnumerateArray().Single(t => t.GetProperty("from").GetString() == "in1");
        fromIn1.GetProperty("to").EnumerateArray().Select(t => t.GetProperty("transmission_db").GetDouble())
            .ShouldAllBe(db => db > -3.2 && db < -2.9, "a 3 dB coupler splits evenly");
    }

    [AvaloniaFact]
    public async Task Optimize_FindsTheSwitchingPhase_AndAppliesItUndoably()
    {
        await using var session = HeadlessAgentSession.Create(TextWriter.Null);
        await Ok(session, "apply_design", Mzi);

        var best = await Ok(session, "optimize",
            """{"parameters":[{"id":"ps","parameter":"phase_shift"}],"objective":{"maximize_ratio":["bar","cross"]}}""");

        (best.GetProperty("best_db").GetDouble() - best.GetProperty("initial_db").GetDouble()).ShouldBeGreaterThan(30);
        var outputs = (await Ok(session, "simulate")).GetProperty("outputs").EnumerateArray()
            .ToDictionary(o => o.GetProperty("name").GetString()!, o => o.GetProperty("power_db").GetDouble());
        (outputs["bar"] - outputs["cross"]).ShouldBeGreaterThan(30, "the applied phase routes the light to bar");
        await Ok(session, "undo");
        (await Ok(session, "get_component", """{"id":"ps"}""")).GetProperty("parameters")[0].GetProperty("value").GetDouble().ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task FiberArray_BringsPortsOutAtThePitch()
    {
        await using var session = HeadlessAgentSession.Create(TextWriter.Null);
        await Ok(session, "new_design");
        await Ok(session, "place_component", """{"type":"2x2 MMI Coupler","x":1500,"y":1500,"name":"dev"}""");

        var array = await Ok(session, "add_fiber_array",
            """{"ports":["dev.in1","dev.in2"],"coupler":"Grating Coupler","pdk":"Demo PDK","side":"west","pitch_um":127}""");

        var couplers = array.GetProperty("couplers").EnumerateArray().ToList();
        couplers.Count.ShouldBe(2);
        (couplers[1].GetProperty("y").GetDouble() - couplers[0].GetProperty("y").GetDouble()).ShouldBe(127, 0.01);
        array.GetProperty("bundle").GetProperty("blocked").GetInt32().ShouldBe(0);
        (await Ok(session, "get_component", """{"id":"fa0"}""")).GetProperty("pins").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "waveguide")
            .GetProperty("angle_deg").GetDouble().ShouldBe(0, "the waveguide pin points east, at the device");
    }

    [AvaloniaFact]
    public async Task CompareNetlist_FindsMissingWiresAndExtraParts()
    {
        await using var session = HeadlessAgentSession.Create(TextWriter.Null);
        await Ok(session, "apply_design", Mzi);
        var reference = (await session.Catalog.InvokeAsync("export_netlist",
            JsonDocument.Parse("""{"format":"gdsfactory_yaml"}""").RootElement.Clone())).Content[0].Text!;
        var args = JsonSerializer.Serialize(new { text = reference });

        (await Ok(session, "compare_netlist", args)).GetProperty("match").GetBoolean().ShouldBeTrue();

        await Ok(session, "delete_connection", """{"id":"comb.out2"}""");
        await Ok(session, "place_component", """{"type":"1x2 MMI Splitter","x":2000,"y":2000}""");
        var diff = await Ok(session, "compare_netlist", args);

        diff.GetProperty("match").GetBoolean().ShouldBeFalse();
        diff.GetProperty("missing_wires")[0].GetString().ShouldContain("2x2 MMI Coupler.out2");
        var splitters = diff.GetProperty("types").EnumerateArray().Single(t => t.GetProperty("type").GetString() == "1x2 MMI Splitter");
        (splitters.GetProperty("layout").GetInt32(), splitters.GetProperty("reference").GetInt32()).ShouldBe((2, 1));
    }

    [AvaloniaFact]
    public async Task FiberArray_WithAnUnknownPort_LeavesNothingBehind()
    {
        await using var session = HeadlessAgentSession.Create(TextWriter.Null);
        await Ok(session, "new_design");
        await Ok(session, "place_component", """{"type":"2x2 MMI Coupler","x":1500,"y":1500,"name":"dev"}""");

        var result = await session.Catalog.InvokeAsync("add_fiber_array", JsonDocument.Parse(
            """{"ports":["dev.in1","dev.nope"],"coupler":"Grating Coupler","pdk":"Demo PDK"}""").RootElement.Clone());

        result.IsError.ShouldBeTrue();
        session.Main.Canvas.Components.ShouldHaveSingleItem();
    }
}
