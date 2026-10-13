using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Hosting;
using CAP.Avalonia.Services.AgentBridge.Transport;
using CAP.Avalonia.ViewModels.Settings.AgentBridge;
using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge;

/// <summary>
/// Persistence of the bridge settings (opt-in, port, per-install token), the lifecycle host,
/// and the settings view-model's status line and connect command.
/// </summary>
public class AgentBridgeSettingsTests : IDisposable
{
    private readonly string _preferencesPath = Path.Combine(Path.GetTempPath(), $"agent-bridge-prefs-{Guid.NewGuid()}.json");
    private readonly UserPreferencesService _preferences;
    private readonly AgentBridgeHost _host;

    public AgentBridgeSettingsTests()
    {
        _preferences = new UserPreferencesService(_preferencesPath);
        _host = new AgentBridgeHost(_preferences, new McpHttpServer(
            new McpHttpEndpoint(AgentBridgeTestStack.Dispatcher(), _preferences.GetOrCreateAgentBridgeToken)));
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

    [Fact]
    public void Defaults_AreOffWithDefaultPort()
    {
        _preferences.GetAgentBridgeEnabled().ShouldBeFalse("the bridge is opt-in");
        _preferences.GetAgentBridgePort().ShouldBe(UserPreferencesService.DefaultAgentBridgePort);
    }

    [Fact]
    public void Token_IsGeneratedOnce_PersistedAndRegenerable()
    {
        var token = _preferences.GetOrCreateAgentBridgeToken();

        token.Length.ShouldBe(64, "256 random bits as hex");
        new UserPreferencesService(_preferencesPath).GetOrCreateAgentBridgeToken().ShouldBe(token);
        _preferences.RegenerateAgentBridgeToken().ShouldNotBe(token);
    }

    [Fact]
    public async Task Host_StartsWhenEnabled_AndStopsWhenDisabled()
    {
        _preferences.SetAgentBridgePort(FreePort());

        await _host.ApplySettingsAsync();
        _host.IsRunning.ShouldBeFalse("disabled by default");

        _preferences.SetAgentBridgeEnabled(true);
        await _host.ApplySettingsAsync();
        _host.IsRunning.ShouldBeTrue();
        _host.Port.ShouldBe(_preferences.GetAgentBridgePort());

        _preferences.SetAgentBridgeEnabled(false);
        await _host.ApplySettingsAsync();
        _host.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task Host_PortInUse_ReportsErrorInsteadOfThrowing()
    {
        var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        try
        {
            _preferences.SetAgentBridgePort(((IPEndPoint)blocker.LocalEndpoint).Port);
            _preferences.SetAgentBridgeEnabled(true);

            await _host.ApplySettingsAsync();

            _host.IsRunning.ShouldBeFalse();
            _host.LastError.ShouldNotBeNullOrEmpty();
        }
        finally
        {
            blocker.Stop();
        }
    }

    [Fact]
    public void ConnectCommand_UsesLoopbackUrlAndBearerHeader()
    {
        var command = AgentBridgeHost.ClaudeCodeConnectCommand(39271, "abc");

        command.ShouldBe(
            "claude mcp add --transport http lunima http://127.0.0.1:39271/mcp --header \"Authorization: Bearer abc\"");
    }

    [Fact]
    public async Task ViewModel_Toggle_StartsServer_AndShowsListeningStatus()
    {
        _preferences.SetAgentBridgePort(FreePort());
        using var vm = new AgentBridgeSettingsViewModel(_preferences, _host);
        vm.IsListening.ShouldBeFalse();

        await vm.ApplyPortCommand.ExecuteAsync(null);
        vm.IsEnabled = true;
        await WaitUntil(() => vm.IsListening);

        vm.StatusText.ShouldContain(AgentBridgeHost.EndpointUrl(_preferences.GetAgentBridgePort()));
        vm.ConnectCommandText.ShouldContain(vm.Token);
        _preferences.GetAgentBridgeEnabled().ShouldBeTrue("the toggle persists");
    }

    [Fact]
    public async Task ViewModel_CopyConnectCommand_UsesClipboardHook()
    {
        using var vm = new AgentBridgeSettingsViewModel(_preferences, _host);
        string? copied = null;
        vm.CopyToClipboard = text =>
        {
            copied = text;
            return Task.CompletedTask;
        };

        await vm.CopyConnectCommandCommand.ExecuteAsync(null);

        copied.ShouldBe(vm.ConnectCommandText);
        vm.FeedbackText.ShouldNotBeNullOrEmpty();
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(20);
        condition().ShouldBeTrue();
    }
}
