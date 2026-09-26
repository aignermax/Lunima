using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP.Avalonia.Services.LiveState;

namespace CAP.Avalonia.ViewModels.LiveState;

/// <summary>
/// ViewModel for the Agent Server window: lets the user start/stop the
/// localhost live-state server that external coding agents connect to via MCP, and
/// shows the endpoint plus a ready-to-paste MCP client configuration.
/// </summary>
public partial class AgentServerViewModel : ObservableObject
{
    /// <summary>Environment variable that auto-starts the server on app launch.</summary>
    public const string AutoStartEnvironmentVariable = "LUNIMA_AGENT_SERVER";

    private readonly LiveStateHttpServer _server;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndpointUrl))]
    [NotifyPropertyChangedFor(nameof(McpConfigSnippet))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndpointUrl))]
    [NotifyPropertyChangedFor(nameof(McpConfigSnippet))]
    private int _port;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>Base URL agents talk to while the server runs.</summary>
    public string EndpointUrl => FormattableString.Invariant($"http://127.0.0.1:{Port}");

    /// <summary>
    /// Ready-to-paste MCP configuration for Claude Code / other MCP clients,
    /// pointing at the stdio bridge in mcp-servers/livestate.
    /// </summary>
    public string McpConfigSnippet => string.Format(CultureInfo.InvariantCulture,
        """
        {{
          "mcpServers": {{
            "lunima-live": {{
              "command": "python3",
              "args": ["<repo>/mcp-servers/livestate/server.py"],
              "env": {{ "LUNIMA_AGENT_PORT": "{0}" }}
            }}
          }}
        }}
        """, Port);

    /// <summary>Initializes the ViewModel around the shared server instance.</summary>
    public AgentServerViewModel(LiveStateHttpServer server)
    {
        _server = server;
        _port = server.Port;
        _isRunning = server.IsRunning;
    }

    /// <summary>Starts the server when stopped, stops it when running.</summary>
    [RelayCommand]
    private void ToggleServer()
    {
        if (_server.IsRunning)
        {
            _server.Stop();
            IsRunning = false;
            StatusText = "Stopped";
            return;
        }

        if (_server.Start(Port))
        {
            IsRunning = true;
            StatusText = $"Listening on {EndpointUrl}";
        }
        else
        {
            IsRunning = false;
            StatusText = FormattableString.Invariant(
                $"Could not bind port {Port} — is it already in use?");
        }
    }

    /// <summary>
    /// Starts the server on app launch when <see cref="AutoStartEnvironmentVariable"/>=1,
    /// so agent-driven sessions need no manual click.
    /// </summary>
    public void AutoStartFromEnvironment()
    {
        if (Environment.GetEnvironmentVariable(AutoStartEnvironmentVariable) != "1" || IsRunning)
            return;
        ToggleServer();
    }
}
