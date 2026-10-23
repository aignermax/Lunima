using System.Globalization;
using System.Net.Sockets;
using CAP.Avalonia.Services.AgentBridge.Transport;

namespace CAP.Avalonia.Services.AgentBridge.Hosting;

/// <summary>
/// Owns the agent bridge's lifecycle: starts, restarts or stops the loopback MCP server so it
/// matches the persisted settings (enabled flag + port), and exposes the status and the
/// connect command for the settings page.
/// </summary>
public sealed class AgentBridgeHost
{
    private readonly UserPreferencesService _preferences;
    private readonly McpHttpServer _server;
    private readonly object _settingsSync = new();
    private readonly SemaphoreSlim _settingsApplication = new(1, 1);
    private long _settingsVersion;
    private bool _retired;

    /// <summary>Creates the host (does not start the server — see <see cref="ApplySettings"/>).</summary>
    public AgentBridgeHost(UserPreferencesService preferences, McpHttpServer server)
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

    /// <summary>The MCP endpoint URL for <paramref name="port"/>.</summary>
    public static string EndpointUrl(int port) =>
        string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}{McpHttpEndpoint.EndpointPath}");

    /// <summary>Name under which Claude Code knows the bridge (<c>claude mcp add … lunima …</c>).</summary>
    public const string McpServerName = "lunima";

    /// <summary>
    /// Arguments of <c>claude mcp add</c> that register the bridge for the current user. The
    /// <c>Authorization</c> header is only added when a token is required (<paramref name="token"/> not null).
    /// </summary>
    public static IReadOnlyList<string> ClaudeCodeAddArguments(int port, string? token)
    {
        var arguments = new List<string>
        {
            "mcp", "add", "--scope", "user", "--transport", "http", McpServerName, EndpointUrl(port),
        };
        if (!string.IsNullOrEmpty(token))
            arguments.AddRange(new[] { "--header", $"Authorization: Bearer {token}" });
        return arguments;
    }

    /// <summary>The copy-paste <c>claude mcp add</c> command that connects Claude Code to the bridge.</summary>
    public static string ClaudeCodeConnectCommand(int port, string? token) =>
        "claude " + string.Join(" ", ClaudeCodeAddArguments(port, token).Select(QuoteIfNeeded));

    private static string QuoteIfNeeded(string argument) =>
        argument.Contains(' ') ? $"\"{argument}\"" : argument;

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
