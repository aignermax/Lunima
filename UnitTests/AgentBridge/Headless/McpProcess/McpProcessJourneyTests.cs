using System.Text.Json;
using CAP_Core.Export.Processes;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.AgentBridge.Headless.McpProcess;

/// <summary>
/// The shipped <c>lunima mcp</c> entry point end to end: the built CAP.Desktop executable
/// (<c>Program.Main</c> → <c>HeadlessAgentProgram</c> → Avalonia headless with Skia → stdout
/// protocol only) serves a whole design journey over stdio the way the Claude Code plugin starts it,
/// then a second process reopens the saved file with <c>lunima agent call</c>. The in-process tool
/// and protocol tests cannot see a broken entry point or a log line leaking onto stdout; this can.
/// </summary>
public sealed class McpProcessJourneyTests
{
    private const string Coupler = "Grating Coupler";
    private const string DemoPdk = "Demo PDK";
    private const string CouplerPin = "waveguide";
    private const string DesignFileName = "journey.lun";
    private const int ExpectedComponents = 2;
    private const int ExpectedConnections = 1;
    private static readonly TimeSpan ExitDeadline = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ReopenTimeout = TimeSpan.FromSeconds(45);

    private static readonly string[] JourneyTools =
    [
        "new_design", "place_component", "create_connection", "set_light_source",
        "simulate", "validate_design", "save_design", "open_design",
    ];

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the server's stderr lands there).</summary>
    public McpProcessJourneyTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task McpServerProcess_BuildsSimulatesValidatesAndSaves_AndTheSavedDesignReopens()
    {
        var build = RequireDesktopBuild();
        await using var sandbox = LunimaSandbox.Create();
        var design = sandbox.PathFor(DesignFileName);

        await ServeJourneyAsync(build, sandbox, design);

        await ReopenedDesignShouldMatchAsync(build, sandbox, design);
    }

    private static LunimaDesktopBuild RequireDesktopBuild()
    {
        var build = LunimaDesktopBuild.Find();
        Skip.If(build == null,
            $"CAP.Desktop is not built at {LunimaDesktopBuild.ExpectedAssemblyPath() ?? "(no repository checkout)"}; " +
            "run `dotnet build CAP.Desktop/CAP.Desktop.csproj` in the test configuration first.");
        build!.StaleReason().ShouldBeNull();
        return build;
    }

    private async Task ServeJourneyAsync(LunimaDesktopBuild build, LunimaSandbox sandbox, string design)
    {
        var server = McpStdioProcessClient.Start(build.StartInfo(sandbox, "mcp"));
        try
        {
            await HandshakeAsync(server);
            await BuildTwoCouplerLinkAsync(server);
            await SimulateAndValidateAsync(server);
            await SaveAsync(server, design);
            await ShouldExitCleanlyAsync(server);
        }
        finally
        {
            await server.DisposeAsync();
            _output.WriteLine($"lunima mcp stderr:\n{server.StandardErrorText}");
        }
    }

    private static async Task HandshakeAsync(McpStdioProcessClient server)
    {
        var initialized = await server.RequestAsync("initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = nameof(McpProcessJourneyTests), version = "1" },
        });
        initialized.GetProperty("serverInfo").GetProperty("name").GetString().ShouldBe("lunima");
        initialized.GetProperty("protocolVersion").GetString().ShouldNotBeNullOrWhiteSpace();
        await server.NotifyAsync("notifications/initialized");

        var tools = (await server.RequestAsync("tools/list", new { })).GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()).ToHashSet();
        JourneyTools.Except(tools).ShouldBeEmpty("tools/list must offer every tool of the design journey");
    }

    private static async Task BuildTwoCouplerLinkAsync(McpStdioProcessClient server)
    {
        await server.CallToolAsync("new_design", new { });
        await server.CallToolAsync("place_component", new { type = Coupler, pdk = DemoPdk, x = 0, y = 0, name = "in" });
        await server.CallToolAsync("place_component",
            new { type = Coupler, pdk = DemoPdk, x = 600, y = 200, rotation_deg = 180, name = "out" });

        var wired = await server.CallToolAsync("create_connection",
            new { from_component = "in", from_pin = CouplerPin, to_component = "out", to_pin = CouplerPin });
        var connection = wired.GetProperty("connection");
        connection.ValueKind.ShouldBe(JsonValueKind.Object, $"no wire was created: {wired}");
        connection.GetProperty("blocked").GetBoolean().ShouldBeFalse($"the route is blocked: {connection}");

        var laser = await server.CallToolAsync("set_light_source", new { id = "out", enabled = false });
        laser.GetProperty("enabled").GetBoolean().ShouldBeFalse();
    }

    private static async Task SimulateAndValidateAsync(McpStdioProcessClient server)
    {
        var simulation = await server.CallToolAsync("simulate", new { });
        simulation.GetProperty("success").GetBoolean().ShouldBeTrue($"{simulation}");
        var output = simulation.GetProperty("outputs").EnumerateArray().ShouldHaveSingleItem();
        var power = output.GetProperty("power").GetDouble();
        power.ShouldBeGreaterThan(0, "light must reach the output coupler");
        power.ShouldBeLessThan(1, "a passive link loses some light");

        var checks = await server.CallToolAsync("validate_design", new { min_severity = "problem" });
        checks.GetProperty("total").GetInt32().ShouldBe(0, $"design checks found problems: {checks}");
    }

    private static async Task SaveAsync(McpStdioProcessClient server, string design)
    {
        var saved = await server.CallToolAsync("save_design", new { path = design });
        File.Exists(design).ShouldBeTrue($"save_design reported {saved} but wrote no file");
        ShouldHaveTheJourneyCounts(saved);
    }

    private static async Task ShouldExitCleanlyAsync(McpStdioProcessClient server)
    {
        // Ownership on Windows is the Job object; the descendant listing below needs ps (Linux/macOS).
        var trackDescendants = !OperatingSystem.IsWindows();
        var descendants = trackDescendants
            ? (await ProcessTreeProbe.SnapshotAsync()).DescendantsOf(server.ProcessId)
            : new HashSet<int>();

        var exitCode = await server.CloseInputAndWaitForExitAsync(ExitDeadline);
        exitCode.ShouldBe(0, "closing stdin ends an MCP session successfully");

        if (trackDescendants)
        {
            var after = await ProcessTreeProbe.SnapshotAsync();
            after.StillAlive(descendants).ShouldBeEmpty("child processes outlived the server");
            after.GroupMembers(server.ProcessId).ShouldBeEmpty("processes of the server's group outlived it");
        }
        await server.DrainStandardOutputAsync();
        server.StdoutLines.Where(line => JsonRpcLine.Violation(line) != null)
            .ShouldBeEmpty("every stdout line must be a JSON-RPC 2.0 message");
    }

    private async Task ReopenedDesignShouldMatchAsync(LunimaDesktopBuild build, LunimaSandbox sandbox, string design)
    {
        var reopen = build.StartInfo(sandbox, "agent", "call", "get_design_summary", "--design", design);
        var run = await OwnedProcessCapture.RunAsync(reopen, ReopenTimeout);
        _output.WriteLine($"lunima agent call stderr:\n{run.Error}");

        run.TimedOut.ShouldBeFalse("`agent call get_design_summary` did not finish");
        run.ExitCode.ShouldBe(0, run.Error);
        using var printed = JsonDocument.Parse(run.Output);
        printed.RootElement.GetProperty("is_error").GetBoolean().ShouldBeFalse(run.Output);
        ShouldHaveTheJourneyCounts(printed.RootElement.GetProperty("result"));
    }

    private static void ShouldHaveTheJourneyCounts(JsonElement design)
    {
        design.GetProperty("component_count").GetInt32().ShouldBe(ExpectedComponents, $"{design}");
        design.GetProperty("connection_count").GetInt32().ShouldBe(ExpectedConnections, $"{design}");
        design.GetProperty("blocked_count").GetInt32().ShouldBe(0, $"{design}");
    }
}
