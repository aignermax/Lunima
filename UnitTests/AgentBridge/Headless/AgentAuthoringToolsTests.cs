using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CAP.Avalonia.Services.AgentBridge.Headless;
using CAP.Avalonia.Services.AgentBridge.Tools;
using Shouldly;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.AgentBridge.Headless;

/// <summary>
/// Building a design from nothing through the authoring tools of a headless session:
/// library lookup, exact placement with rotation, explicit and automatic pin wiring,
/// parameters, light sources, and undo of each step.
/// </summary>
public class AgentAuthoringToolsTests
{
    private const string Coupler = "Grating Coupler";
    private const string Splitter = "1x2 MMI Splitter";
    private const string DemoPdk = "Demo PDK";

    private static async Task<JsonElement> Ok(HeadlessAgentSession session, string tool, string args = "{}")
    {
        var result = await session.Catalog.InvokeAsync(tool, JsonDocument.Parse(args).RootElement.Clone());
        result.IsError.ShouldBeFalse(result.Content[0].Text);
        return JsonDocument.Parse(result.Content[0].Text!).RootElement.Clone();
    }

    private static async Task<string> Fails(HeadlessAgentSession session, string tool, string args)
    {
        var result = await session.Catalog.InvokeAsync(tool, JsonDocument.Parse(args).RootElement.Clone());
        result.IsError.ShouldBeTrue();
        return result.Content[0].Text!;
    }

    private static Task<JsonElement> Place(HeadlessAgentSession session, string type, double x, double y, string name, int rotation = 0) =>
        Ok(session, "place_component",
            $$"""{"type":"{{type}}","pdk":"{{DemoPdk}}","x":{{x}},"y":{{y}},"rotation_deg":{{rotation}},"name":"{{name}}"}""");

    private static async Task<HeadlessAgentSession> NewSession()
    {
        var session = HeadlessAgentSession.Create(TextWriter.Null);
        await Ok(session, "new_design");
        return session;
    }

    [AvaloniaFact]
    public async Task ListComponentTypes_FiltersAndDescribesPins()
    {
        await using var session = await NewSession();

        var listing = await Ok(session, "list_component_types", $$"""{"filter":"{{Splitter}}","pdk":"{{DemoPdk}}"}""");

        var type = listing.GetProperty("types").EnumerateArray().Single();
        type.GetProperty("name").GetString().ShouldBe(Splitter);
        type.GetProperty("pins").EnumerateArray().Select(p => p.GetProperty("name").GetString())
            .ShouldBe(new[] { "in", "out1", "out2" });
        type.GetProperty("parameters").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [AvaloniaFact]
    public async Task Place_PutsTopLeftExactly_WithRotation_AndIsUndoable()
    {
        await using var session = await NewSession();

        var placed = (await Place(session, Coupler, 100, 200, "in", rotation: 180)).GetProperty("placed");

        placed.GetProperty("x").GetDouble().ShouldBe(100);
        placed.GetProperty("y").GetDouble().ShouldBe(200);
        placed.GetProperty("rotation_deg").GetDouble().ShouldBe(180);
        placed.GetProperty("name").GetString().ShouldBe("in");
        session.Main.Canvas.Components.Count.ShouldBe(1);

        await Ok(session, "undo");
        await Ok(session, "undo");
        session.Main.Canvas.Components.ShouldBeEmpty("placement and naming are both undoable");
    }

    [AvaloniaFact]
    public async Task Place_OnAnOccupiedSpot_IsRefused_UnlessNudged()
    {
        await using var session = await NewSession();
        await Place(session, Splitter, 0, 0, "a");

        (await Fails(session, "place_component", $$"""{"type":"{{Splitter}}","x":10,"y":10}""")).ShouldContain("occupied");

        var nudged = await Ok(session, "place_component", $$"""{"type":"{{Splitter}}","x":10,"y":10,"nudge":true}""");
        nudged.GetProperty("nudged").GetBoolean().ShouldBeTrue();
        session.Main.Canvas.Components.Count.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task Place_UnknownType_ExplainsHowToFindTypes() =>
        (await Fails(await NewSession(), "place_component", """{"type":"Flux Capacitor","x":0,"y":0}"""))
            .ShouldContain("list_component_types");

    [AvaloniaFact]
    public async Task Connect_NamedAndAutomaticPins_RouteAWire()
    {
        await using var session = await NewSession();
        await Place(session, Coupler, 0, 200, "in");
        await Place(session, Splitter, 300, 180, "split");
        await Place(session, Coupler, 700, 100, "out", rotation: 180);

        var automatic = await Ok(session, "create_connection", """{"from_component":"in","to_component":"split"}""");
        automatic.GetProperty("to").GetString().ShouldEndWith(".in", customMessage: "the facing free pin is chosen");
        var named = await Ok(session, "create_connection",
            """{"from_component":"split","from_pin":"out1","to_component":"out","to_pin":"waveguide"}""");

        named.GetProperty("connection").GetProperty("blocked").GetBoolean().ShouldBeFalse();
        named.GetProperty("connection").GetProperty("length_um").GetDouble().ShouldBeGreaterThan(0);
        session.Main.Canvas.Connections.Count.ShouldBe(2);

        var splitter = await Ok(session, "get_component", """{"id":"split"}""");
        splitter.GetProperty("pins").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "out1")
            .GetProperty("connected_to").GetProperty("pin").GetString().ShouldEndWith(".waveguide");
    }

    [AvaloniaFact]
    public async Task Connect_UnknownPin_ListsTheValidOnes()
    {
        await using var session = await NewSession();
        await Place(session, Coupler, 0, 200, "in");
        await Place(session, Splitter, 300, 180, "split");

        (await Fails(session, "create_connection", """{"from_component":"in","to_component":"split","to_pin":"nope"}"""))
            .ShouldContain("out1");
    }

    [AvaloniaFact]
    public async Task SetParameter_ByName_ClampsAndUndoes()
    {
        await using var session = await NewSession();
        await Place(session, Splitter, 0, 0, "split");

        var set = await Ok(session, "set_parameter", """{"id":"split","parameter":"splitting_ratio","value":150}""");

        set.GetProperty("value").GetDouble().ShouldBe(100, "clamped to the parameter's maximum");
        set.GetProperty("clamped").GetBoolean().ShouldBeTrue();
        var previous = set.GetProperty("previous").GetDouble();
        await Ok(session, "undo");
        var slider = session.Main.Canvas.Components.Single().Component.GetSlider(set.GetProperty("slider").GetInt32())!;
        slider.Value.ShouldBe(previous);
    }

    [AvaloniaFact]
    public async Task SetLightSource_ConfiguresTheLaser_AndUndoes()
    {
        await using var session = await NewSession();
        await Place(session, Coupler, 0, 0, "in");

        var set = await Ok(session, "set_light_source", """{"id":"in","enabled":false,"wavelength_nm":1310,"power":0.5}""");

        set.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        set.GetProperty("wavelength_nm").GetInt32().ShouldBe(1310);
        set.GetProperty("power").GetDouble().ShouldBe(0.5);
        await Ok(session, "undo");
        var laser = session.Main.Canvas.Components.Single().LaserConfig!;
        laser.IsEnabled.ShouldBeTrue();
        laser.WavelengthNm.ShouldNotBe(1310);
    }

    [AvaloniaFact]
    public async Task SetLightSource_OnANonSource_NeedsTheMark()
    {
        await using var session = await NewSession();
        await Place(session, Splitter, 0, 0, "split");

        (await Fails(session, "set_light_source", """{"id":"split","enabled":true}""")).ShouldContain("mark_as_source");
    }

    [AvaloniaFact]
    public async Task Rotate_TurnsCounterClockwise_AndRefusesLockedParts()
    {
        await using var session = await NewSession();
        await Place(session, Splitter, 0, 0, "split");

        var rotated = await Ok(session, "rotate_component", """{"id":"split","degrees":270}""");

        rotated.GetProperty("rotated").GetProperty("rotation_deg").GetDouble().ShouldBe(270);
        session.Main.Canvas.Components.Single().Component.IsLocked = true;
        (await Fails(session, "rotate_component", """{"id":"split"}""")).ShouldContain("locked");
    }

    [AvaloniaFact]
    public async Task OpenDesign_RejectsMissingAndForeignFiles()
    {
        await using var session = await NewSession();

        (await Fails(session, "open_design", """{"path":"/nonexistent/x.lun"}""")).ShouldContain("not found");
        (await Fails(session, "open_design", """{"path":"chip.gds"}""")).ShouldContain("import_gds");
    }
}
