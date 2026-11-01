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

/// <summary>
/// Structured analysis through agent tools on a headless session: simulation outputs,
/// parameter and wavelength sweeps, and the design checks.
/// </summary>
public class AgentAnalysisToolsTests
{
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

    /// <summary>Input coupler → MZI (splitter, phase shifter arm, 2×2 combiner) → two output couplers.</summary>
    private static async Task<HeadlessAgentSession> Mzi()
    {
        var session = HeadlessAgentSession.Create(TextWriter.Null);
        await Ok(session, "new_design");
        await Ok(session, "place_component", """{"type":"Grating Coupler","pdk":"Demo PDK","x":0,"y":200,"name":"in"}""");
        await Ok(session, "place_component", """{"type":"1x2 MMI Splitter","x":200,"y":180,"name":"split"}""");
        await Ok(session, "place_component", """{"type":"Phase Shifter","x":400,"y":60,"name":"ps"}""");
        await Ok(session, "place_component", """{"type":"2x2 MMI Coupler","x":800,"y":170,"name":"comb"}""");
        await Ok(session, "place_component", """{"type":"Grating Coupler","pdk":"Demo PDK","x":1200,"y":120,"rotation_deg":180,"name":"bar"}""");
        await Ok(session, "place_component", """{"type":"Grating Coupler","pdk":"Demo PDK","x":1200,"y":300,"rotation_deg":180,"name":"cross"}""");
        await Ok(session, "create_connection", """{"from_component":"in","to_component":"split"}""");
        await Ok(session, "create_connection", """{"from_component":"split","from_pin":"out1","to_component":"ps"}""");
        await Ok(session, "create_connection", """{"from_component":"ps","to_component":"comb","to_pin":"in1"}""");
        await Ok(session, "create_connection", """{"from_component":"split","from_pin":"out2","to_component":"comb","to_pin":"in2"}""");
        await Ok(session, "create_connection", """{"from_component":"comb","from_pin":"out1","to_component":"bar"}""");
        await Ok(session, "create_connection", """{"from_component":"comb","from_pin":"out2","to_component":"cross"}""");
        await Ok(session, "set_light_source", """{"id":"in","enabled":true,"wavelength_nm":1550,"power":1}""");
        await Ok(session, "set_light_source", """{"id":"bar","enabled":false}""");
        await Ok(session, "set_light_source", """{"id":"cross","enabled":false}""");
        return session;
    }

    [AvaloniaFact]
    public async Task Simulate_ReportsSourcesAndEveryOutput()
    {
        await using var session = await Mzi();

        var report = await Ok(session, "simulate", """{"include_connections":true}""");

        report.GetProperty("sources").GetArrayLength().ShouldBe(1);
        var outputs = report.GetProperty("outputs").EnumerateArray().ToList();
        outputs.Select(o => o.GetProperty("name").GetString()).ShouldBe(new[] { "bar", "cross" }, ignoreOrder: true);
        var total = outputs.Sum(o => o.GetProperty("power").GetDouble());
        total.ShouldBeGreaterThan(0.01, "light reaches the outputs");
        total.ShouldBeLessThanOrEqualTo(1.0 + 1e-9, "a passive circuit cannot amplify");
        report.GetProperty("connections").GetArrayLength().ShouldBe(6);
    }

    [AvaloniaFact]
    public async Task SweepParameter_ShowsComplementaryInterference_AndRestoresTheValue()
    {
        await using var session = await Mzi();
        var before = (await Ok(session, "get_component", """{"id":"ps"}""")).GetProperty("parameters")[0].GetProperty("value").GetDouble();

        var sweep = await Ok(session, "sweep_parameter", """{"id":"ps","steps":9}""");

        var outputs = sweep.GetProperty("outputs").EnumerateArray().ToList();
        outputs.Count.ShouldBe(2);
        foreach (var output in outputs)
            (output.GetProperty("max_db").GetDouble() - output.GetProperty("min_db").GetDouble())
                .ShouldBeGreaterThan(20, "an MZI output swings from bright to dark with the phase");
        outputs[0].GetProperty("max_at").GetDouble().ShouldNotBe(outputs[1].GetProperty("max_at").GetDouble(),
            "bar and cross are bright at different phases");
        var after = (await Ok(session, "get_component", """{"id":"ps"}""")).GetProperty("parameters")[0].GetProperty("value").GetDouble();
        after.ShouldBe(before);
    }

    [AvaloniaFact]
    public async Task SweepSpectrum_FindsTheRingResonance()
    {
        await using var session = HeadlessAgentSession.Create(TextWriter.Null);
        var examples = new ExampleDesignsService(new FileExampleCatalog()).FindExamplesDirectory()!;
        await session.OpenDesignAsync(Path.Combine(examples, "EBeam Add-Drop Ring.lun"));

        var spectrum = await Ok(session, "sweep_spectrum", """{"start_nm":1540,"end_nm":1560,"steps":401}""");

        var drop = spectrum.GetProperty("outputs").EnumerateArray().Single(o => o.GetProperty("output").GetString() == "gc_drop");
        drop.GetProperty("extinction_db").GetDouble().ShouldBeGreaterThan(10);
        drop.GetProperty("fsr_nm").GetDouble().ShouldBeInRange(5, 15);
        drop.GetProperty("transmission_db").GetArrayLength().ShouldBe(drop.GetProperty("wavelengths_nm").GetArrayLength());
    }

    [AvaloniaFact]
    public async Task ValidateDesign_ReportsOpenFiberPinsAsHints()
    {
        await using var session = await Mzi();

        var checks = await Ok(session, "validate_design");

        checks.GetProperty("findings").EnumerateArray()
            .Count(f => f.GetProperty("kind").GetString() == "UnconnectedPin").ShouldBe(3, "the three fiber pins are open");
        (await Ok(session, "validate_design", """{"min_severity":"problem"}""")).GetProperty("total").GetInt32().ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Analyses_ExplainWhatIsMissing()
    {
        await using var session = HeadlessAgentSession.Create(TextWriter.Null);
        await Ok(session, "new_design");

        (await Fails(session, "simulate")).ShouldContain("No components");
        (await Fails(session, "sweep_spectrum", """{"start_nm":1500,"end_nm":1600}""")).ShouldContain("empty");
        (await Fails(session, "validate_design", """{"min_severity":"fatal"}""")).ShouldContain("hint or problem");
    }
}
