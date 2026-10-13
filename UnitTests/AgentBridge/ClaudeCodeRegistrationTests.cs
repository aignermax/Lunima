using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Hosting;
using CAP.Avalonia.Services.AgentBridge.Registration;
using CAP.Avalonia.Services.AgentBridge.Transport;
using CAP.Avalonia.ViewModels.Settings.AgentBridge;
using ProcessLaunchFactory = CAP_Core.Export.ProcessLaunchFactory;
using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge;

/// <summary>
/// One-click Claude Code registration: CLI discovery, the exact <c>claude mcp</c> calls built
/// through <see cref="ProcessLaunchFactory"/> (with a recording runner instead of real
/// processes), the "CLI not found" fallback, the connector enabling the bridge, the startup
/// prompt and the installer command-line switches.
/// </summary>
public class ClaudeCodeRegistrationTests : IDisposable
{
    private static readonly string Home = Path.Combine(Path.GetTempPath(), "fake-home");
    private static readonly string CliPath = Path.Combine(Home, "bin", "claude");

    private readonly string _preferencesPath = Path.Combine(Path.GetTempPath(), $"claude-reg-prefs-{Guid.NewGuid()}.json");
    private readonly UserPreferencesService _preferences;
    private readonly AgentBridgeHost _host;
    private readonly List<ProcessStartInfo> _calls = new();
    private int _exitCode;

    public ClaudeCodeRegistrationTests()
    {
        _preferences = new UserPreferencesService(_preferencesPath);
        _preferences.SetAgentBridgePort(FreePort());
        _host = new AgentBridgeHost(_preferences, new McpHttpServer(
            new McpHttpEndpoint(AgentBridgeTestStack.Dispatcher(), _preferences.GetRequiredAgentBridgeToken)));
    }

    public void Dispose()
    {
        _host.StopAsync().GetAwaiter().GetResult();
        File.Delete(_preferencesPath);
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static ClaudeCliLocator Locator(params string[] existing) =>
        new(path => existing.Contains(path), () => Path.GetDirectoryName(CliPath), _ => Home, isWindows: false, isMacOs: false);

    private ClaudeCodeRegistrar Registrar(ClaudeCliLocator locator) =>
        new(ProcessLaunchFactory.CreateDefault(), locator, (info, _, _) =>
        {
            _calls.Add(info);
            return Task.FromResult(new CliRunResult(_exitCode, "ok"));
        });

    [Fact]
    public void Locator_PrefersPath_ThenWellKnownLocations()
    {
        var pathDir = Path.Combine(Path.GetTempPath(), "on-path");
        var onPath = Path.Combine(pathDir, "claude");
        var wellKnown = Path.Combine(Home, ".local", "bin", "claude");
        var locator = new ClaudeCliLocator(p => p == onPath || p == wellKnown, () => pathDir, _ => Home, false, false);

        locator.Locate().ShouldBe(onPath);
        Locator(wellKnown).Locate().ShouldBe(wellKnown);
        Locator().Locate().ShouldBeNull();
    }

    [Fact]
    public void Locator_OnWindows_FindsTheNpmCmdShim()
    {
        var appData = Path.Combine(Home, "AppData", "Roaming");
        var shim = Path.Combine(appData, "npm", "claude.cmd");
        var locator = new ClaudeCliLocator(p => p == shim, () => null,
            f => f == Environment.SpecialFolder.ApplicationData ? appData : Home, isWindows: true, isMacOs: false);

        locator.Locate().ShouldBe(shim);
    }

    [Fact]
    public async Task Register_RemovesThenAddsUserScopedServer_WithoutHeaderByDefault()
    {
        var result = await Registrar(Locator(CliPath)).RegisterAsync(39271, token: null);

        result.Success.ShouldBeTrue();
        _calls.Count.ShouldBe(2);
        _calls.ShouldAllBe(c => c.FileName == CliPath && !c.UseShellExecute);
        _calls[0].ArgumentList.ShouldBe(new[] { "mcp", "remove", "--scope", "user", "lunima" });
        _calls[1].ArgumentList.ShouldBe(new[]
            { "mcp", "add", "--scope", "user", "--transport", "http", "lunima", "http://127.0.0.1:39271/mcp" });
    }

    [Fact]
    public async Task Register_WithToken_AddsAuthorizationHeader()
    {
        await Registrar(Locator(CliPath)).RegisterAsync(40000, token: "secret");

        _calls[1].ArgumentList.TakeLast(2).ShouldBe(new[] { "--header", "Authorization: Bearer secret" });
    }

    [Fact]
    public async Task Register_CliMissing_ReportsNotFound_WithoutRunningAnything()
    {
        var result = await Registrar(Locator()).RegisterAsync(39271, null);

        result.CliFound.ShouldBeFalse();
        result.Success.ShouldBeFalse();
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Register_CliFails_ReportsFailureWithOutput()
    {
        _exitCode = 1;

        var result = await Registrar(Locator(CliPath)).RegisterAsync(39271, null);

        result.CliFound.ShouldBeTrue();
        result.Success.ShouldBeFalse();
        result.Output.ShouldBe("ok");
    }

    [Fact]
    public async Task IsRegistered_UsesMcpGet_AndIsNullWithoutCli()
    {
        (await Registrar(Locator(CliPath)).IsRegisteredAsync()).ShouldBe(true);
        _calls.Single().ArgumentList.ShouldBe(new[] { "mcp", "get", "lunima" });

        (await Registrar(Locator()).IsRegisteredAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task Connector_AlwaysEnablesAndStartsTheBridge_EvenWhenCliIsMissing()
    {
        var connector = new ClaudeCodeConnector(_preferences, _host, Registrar(Locator()));

        var result = await connector.ConnectAsync();

        result.CliFound.ShouldBeFalse();
        _preferences.GetAgentBridgeEnabled().ShouldBeTrue("a registered but stopped bridge only yields 'connection refused'");
        _host.IsRunning.ShouldBeTrue();
        connector.ManualCommand.ShouldStartWith("claude mcp add --scope user");
    }

    [Fact]
    public async Task SettingsConnect_ShowsSuccess_OrTheNotFoundFallback()
    {
        using var found = new AgentBridgeSettingsViewModel(_preferences, _host,
            new ClaudeCodeConnector(_preferences, _host, Registrar(Locator(CliPath))));
        await found.ConnectClaudeCodeCommand.ExecuteAsync(null);
        found.IsEnabled.ShouldBeTrue();
        found.IsFeedbackError.ShouldBeFalse();

        using var missing = new AgentBridgeSettingsViewModel(_preferences, _host,
            new ClaudeCodeConnector(_preferences, _host, Registrar(Locator())));
        await missing.ConnectClaudeCodeCommand.ExecuteAsync(null);
        missing.IsFeedbackError.ShouldBeTrue();
        missing.FeedbackText.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Prompt_ShowsOnlyWhenCliPresentAndNotRegistered_AndNotNowLastsForTheVersion()
    {
        _exitCode = 1; // `claude mcp get lunima` fails: not registered yet
        var connector = new ClaudeCodeConnector(_preferences, _host, Registrar(Locator(CliPath)));
        var prompt = new ClaudeCodeConnectPromptViewModel(_preferences, connector, "1.2.3");

        await prompt.CheckOnStartupAsync();
        prompt.IsVisible.ShouldBeTrue();

        prompt.NotNowCommand.Execute(null);
        prompt.IsVisible.ShouldBeFalse();
        var again = new ClaudeCodeConnectPromptViewModel(_preferences, connector, "1.2.3");
        await again.CheckOnStartupAsync();
        again.IsVisible.ShouldBeFalse("dismissed for this version");

        var nextVersion = new ClaudeCodeConnectPromptViewModel(_preferences, connector, "1.2.4");
        await nextVersion.CheckOnStartupAsync();
        nextVersion.IsVisible.ShouldBeTrue("offered again after an update");
    }

    [Fact]
    public async Task Prompt_StaysHidden_WithoutCli_OrWhenAlreadyRegistered()
    {
        var noCli = new ClaudeCodeConnectPromptViewModel(_preferences,
            new ClaudeCodeConnector(_preferences, _host, Registrar(Locator())), "1.0.0");
        await noCli.CheckOnStartupAsync();
        noCli.IsVisible.ShouldBeFalse();

        _exitCode = 0;
        var registered = new ClaudeCodeConnectPromptViewModel(_preferences,
            new ClaudeCodeConnector(_preferences, _host, Registrar(Locator(CliPath))), "1.0.0");
        await registered.CheckOnStartupAsync();
        registered.IsVisible.ShouldBeFalse();
    }

    [Theory]
    [InlineData(new[] { "--register-claude-code" }, true)]
    [InlineData(new[] { "--unregister-claude-code" }, true)]
    [InlineData(new[] { "design.lun" }, false)]
    [InlineData(new string[0], false)]
    public void CommandLine_RecognizesOnlyTheInstallerSwitches(string[] args, bool expected)
    {
        AgentBridgeCommandLine.IsRegistrationCommand(args).ShouldBe(expected);
    }
}
