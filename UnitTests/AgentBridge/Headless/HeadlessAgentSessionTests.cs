using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Headless;
using Shouldly;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.AgentBridge.Headless;

/// <summary>
/// The windowless application: the production container driven only through agent tools,
/// with the design loaded and saved by the headless session and the CLI runner.
/// </summary>
public class HeadlessAgentSessionTests
{
    private const string ExampleFileName = "Mach-Zehnder Interferometer.lun";

    private static string ExamplePath() =>
        Path.Combine(new ExampleDesignsService(new FileExampleCatalog()).FindExamplesDirectory()!, ExampleFileName);

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [AvaloniaFact]
    public async Task Session_OpensDesign_RunsTools_AndSaves()
    {
        var savePath = Path.Combine(Path.GetTempPath(), $"headless-{System.Guid.NewGuid():N}.lun");
        await using var session = HeadlessAgentSession.Create(TextWriter.Null);
        try
        {
            await session.OpenDesignAsync(ExamplePath());

            var summary = await session.Catalog.InvokeAsync("get_design_summary", Args("{}"));
            summary.IsError.ShouldBeFalse();
            var body = JsonDocument.Parse(summary.Content[0].Text!).RootElement;
            body.GetProperty("component_count").GetInt32().ShouldBe(session.Main.Canvas.Components.Count);
            body.GetProperty("component_count").GetInt32().ShouldBeGreaterThan(0);

            (await session.Catalog.InvokeAsync("run_simulation", Args("{}"))).IsError.ShouldBeFalse();

            await session.SaveDesignAsync(savePath);
            File.Exists(savePath).ShouldBeTrue();
        }
        finally
        {
            if (File.Exists(savePath)) File.Delete(savePath);
        }
    }

    [AvaloniaFact]
    public async Task Runner_Call_PrintsJsonResult_AndReportsToolErrors()
    {
        await using var session = HeadlessAgentSession.Create(TextWriter.Null);
        var output = new StringWriter();
        var runner = new AgentCliRunner(session, TextReader.Null, output);

        var ok = await runner.RunAsync(
            new AgentCliCommand(AgentCliVerb.Call, "get_design_summary", DesignPath: ExamplePath()),
            CancellationToken.None);

        ok.ShouldBe(AgentCliRunner.Success);
        var printed = JsonDocument.Parse(output.ToString()).RootElement;
        printed.GetProperty("tool").GetString().ShouldBe("get_design_summary");
        printed.GetProperty("result").GetProperty("file_name").GetString().ShouldBe(ExampleFileName);

        var unknown = await runner.RunAsync(new AgentCliCommand(AgentCliVerb.Call, "no_such_tool"), CancellationToken.None);
        unknown.ShouldBe(AgentCliRunner.UsageError);
    }

    [AvaloniaFact]
    public async Task Runner_Script_StopsAtTheFirstFailedStep()
    {
        await using var session = HeadlessAgentSession.Create(TextWriter.Null);
        var output = new StringWriter();
        var script = """[{"tool":"undo"},{"tool":"get_design_summary"}]""";
        var runner = new AgentCliRunner(session, new StringReader(script), output);

        var exitCode = await runner.RunAsync(new AgentCliCommand(AgentCliVerb.Run, ScriptPath: "-"), CancellationToken.None);

        exitCode.ShouldBe(AgentCliRunner.ToolFailed, "undo on a fresh design has nothing to undo");
        var steps = JsonDocument.Parse(output.ToString()).RootElement;
        steps.GetArrayLength().ShouldBe(1);
        steps[0].GetProperty("is_error").GetBoolean().ShouldBeTrue();
    }
}
