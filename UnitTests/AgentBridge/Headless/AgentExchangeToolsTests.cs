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
/// Design exchange through agent tools: netlist exports (gdsfactory YAML, SAX, Verilog-A),
/// the Nazca script export, and GDS analysis and import.
/// </summary>
public class AgentExchangeToolsTests
{
    private const string MziExample = "Mach-Zehnder Interferometer.lun";

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ConnectAPICPro.sln")))
            directory = directory.Parent;
        return Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray());
    }

    private static string TempDirectory() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "lunima-exchange-" + System.Guid.NewGuid().ToString("N"))).FullName;

    private static async Task<HeadlessAgentSession> OpenMzi()
    {
        var session = HeadlessAgentSession.Create(TextWriter.Null);
        var examples = new ExampleDesignsService(new FileExampleCatalog()).FindExamplesDirectory()!;
        await session.OpenDesignAsync(Path.Combine(examples, MziExample));
        return session;
    }

    private static async Task<(bool IsError, string Text)> Call(HeadlessAgentSession session, string tool, string args)
    {
        var result = await session.Catalog.InvokeAsync(tool, JsonDocument.Parse(args).RootElement.Clone());
        return (result.IsError, result.Content[0].Text!);
    }

    private static string Json(string path) => JsonSerializer.Serialize(path);

    [AvaloniaFact]
    public async Task ExportNetlist_GdsFactoryYaml_ListsInstancesAndConnections()
    {
        await using var session = await OpenMzi();

        var (isError, yaml) = await Call(session, "export_netlist", """{"format":"gdsfactory_yaml"}""");

        isError.ShouldBeFalse(yaml);
        yaml.ShouldContain("instances:");
        yaml.ShouldContain("connections:");
        yaml.ShouldContain("mzi_splitter");
    }

    [AvaloniaFact]
    public async Task ExportNetlist_SaxAndVerilogA_ResolveParametricComponents()
    {
        await using var session = await OpenMzi();
        var folder = TempDirectory();
        try
        {
            var sax = Path.Combine(folder, "mzi_sax.py");
            var (saxError, saxText) = await Call(session, "export_netlist", $$"""{"format":"sax","path":{{Json(sax)}}}""");
            saxError.ShouldBeFalse(saxText);
            File.ReadAllText(sax).ShouldContain("mzi_splitter_model", customMessage: "the parametric MMI exports its evaluated S-matrix");

            var va = Path.Combine(folder, "va");
            var (vaError, vaText) = await Call(session, "export_netlist", $$"""{"format":"verilog_a","path":{{Json(va)}}}""");
            vaError.ShouldBeFalse(vaText);
            Directory.GetFiles(va, "*.va").Length.ShouldBeGreaterThan(1);
            Directory.GetFiles(va, "*_tb.sp").ShouldHaveSingleItem();
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [AvaloniaFact]
    public async Task ExportGds_WritesTheNazcaScript_AndRejectsShadowingNames()
    {
        await using var session = await OpenMzi();
        var folder = TempDirectory();
        try
        {
            var script = Path.Combine(folder, "chip1.py");
            var (isError, text) = await Call(session, "export_gds", $$"""{"path":{{Json(script)}},"generate_gds":false}""");

            isError.ShouldBeFalse(text);
            File.ReadAllText(script).ShouldContain("nazca");
            JsonDocument.Parse(text).RootElement.GetProperty("gds_path").ValueKind.ShouldBe(JsonValueKind.Null);

            var (shadowError, reason) = await Call(session, "export_gds", $$"""{"path":{{Json(Path.Combine(folder, "numpy.py"))}}}""");
            shadowError.ShouldBeTrue();
            reason.ShouldContain("shadow");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [AvaloniaFact]
    public async Task AnalyzeAndImportGds_PlaceTheCellsAndJoinTheirAbutments()
    {
        await using var session = HeadlessAgentSession.Create(TextWriter.Null);
        var gds = Json(RepoFile("Tools", "gds-test-data", "gdsfactory_mzi_like.gds"));

        var (analysisError, analysisText) = await Call(session, "analyze_gds", $$"""{"path":{{gds}}}""");
        analysisError.ShouldBeFalse(analysisText);
        var analysis = JsonDocument.Parse(analysisText).RootElement;
        analysis.GetProperty("top_cells")[0].GetProperty("name").GetString().ShouldBe("gdsfactory_mzi_like");
        analysis.GetProperty("layers").EnumerateArray().ShouldContain(l => l.GetProperty("layer").GetInt32() == 1);

        var (importError, importText) = await Call(session, "import_gds", $$"""{"path":{{gds}}}""");
        importError.ShouldBeFalse(importText);
        var import = JsonDocument.Parse(importText).RootElement;
        import.GetProperty("placed").GetInt32().ShouldBe(4);
        import.GetProperty("connected").GetInt32().ShouldBeGreaterThan(0);
        session.Main.Canvas.Components.ShouldHaveSingleItem("the import is wrapped in one group");
    }

    [AvaloniaFact]
    public async Task GdsTools_ExplainBadInput()
    {
        await using var session = HeadlessAgentSession.Create(TextWriter.Null);
        var gds = Json(RepoFile("Tools", "gds-test-data", "gdsfactory_mzi_like.gds"));

        (await Call(session, "analyze_gds", """{"path":"/no/such.gds"}""")).Text.ShouldContain("not found");
        (await Call(session, "import_gds", $$"""{"path":{{gds}},"port_layers":"1;x"}""")).Text.ShouldContain("port_layers");
        (await Call(session, "import_gds", $$"""{"path":{{gds}},"top_cell":"nope"}""")).Text.ShouldContain("gdsfactory_mzi_like");
    }
}
