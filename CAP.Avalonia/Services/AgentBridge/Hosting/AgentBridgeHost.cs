using CAP_Core.Preferences;
using CAP.Avalonia.Services.AgentBridge.Contracts;
using System.Net.Sockets;
using CAP.Avalonia.Services.AgentBridge.Transport;

namespace CAP.Avalonia.Services.AgentBridge.Hosting;

/// <summary>
/// Owns the agent bridge's lifecycle: starts, restarts or stops the loopback MCP server so it
/// matches the persisted settings (enabled flag + port), and exposes the status and the
/// connect command for the settings page.
/// </summary>
public sealed class AgentBridgeHost : IAgentBridgeControl
{
    private readonly IAgentBridgePreferences _preferences;
    private readonly McpHttpServer _server;
    private readonly object _settingsSync = new();
    private readonly SemaphoreSlim _settingsApplication = new(1, 1);
    private long _settingsVersion;
    private bool _retired;

    /// <summary>Creates the host (does not start the server — see <see cref="ApplySettings"/>).</summary>
    public AgentBridgeHost(IAgentBridgePreferences preferences, McpHttpServer server)
    {
        _preferences = preferences;
        _server = server;
    }

    /// <summary>Raised after the server started, stopped or failed to start.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Whether the server is listening.</summary>
    public bool IsRunning => _server.IsRunning;

    /// <summary>The port the server is bound to (0 when stopped).</summary>
    public int Port => _server.Port;

    /// <summary>Why the last start failed (e.g. port in use), or null.</summary>
    public string? LastError { get; private set; }

    public static string EndpointUrl(int port) => AgentBridgeAddress.EndpointUrl(port);
    public const string McpServerName = AgentBridgeAddress.ServerName;
    public static IReadOnlyList<string> ClaudeCodeAddArguments(int port, string? token) =>
        AgentBridgeAddress.AddArguments(port, token);
    public static string ClaudeCodeConnectCommand(int port, string? token) =>
        AgentBridgeAddress.ConnectCommand(port, token);

    /// <summary>
    /// Brings the server in line with the persisted settings: starts it when enabled,
    /// restarts it on a port change, stops it when disabled. Never throws for a busy port —
    /// the failure is reported via <see cref="LastError"/>.
    /// </summary>
    public Task ApplySettingsAsync()
    {
        lock (_settingsSync)
        {
            if (_retired) return Task.CompletedTask;
            var version = ++_settingsVersion;
            var enabled = _preferences.GetAgentBridgeEnabled();
            var port = _preferences.GetAgentBridgePort();
            var stopping = !enabled || !_server.IsRunning || _server.Port != port
                ? _server.StopAsync() : Task.CompletedTask;
            return ApplySettingsCoreAsync(version, enabled, port, stopping);
        }
    }

    private async Task ApplySettingsCoreAsync(long version, bool enabled, int port, Task stopping)
    {
        await _settingsApplication.WaitAsync();
        try
        {
            await stopping;
            lock (_settingsSync)
            {
                if (_retired || version != _settingsVersion) return;
                if (enabled && !_server.IsRunning) TryStart(port);
                else if (!enabled) LastError = null;
            }
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _settingsApplication.Release(); }
    }

    /// <summary>Fire-and-forget variant of <see cref="ApplySettingsAsync"/> for startup and UI toggles.</summary>
    public void ApplySettings() => _ = ApplySettingsAsync();

    /// <summary>Stops the server (application shutdown).</summary>
    public Task StopAsync()
    {
        lock (_settingsSync)
        {
            _settingsVersion++;
            return _server.StopAsync();
        }
    }

    /// <summary>Permanently retires application admission and drains accepted requests.</summary>
    public Task RetireAsync()
    {
        lock (_settingsSync)
        {
            _retired = true;
            _settingsVersion++;
            return _server.StopAsync();
        }
    }

    private void TryStart(int port)
    {
        try
        {
            _server.Start(port);
            LastError = null;
        }
        catch (SocketException ex)
        {
            LastError = ex.Message;
        }
    }
}
