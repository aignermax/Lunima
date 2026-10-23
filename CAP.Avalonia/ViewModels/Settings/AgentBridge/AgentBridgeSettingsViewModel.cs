using CAP_Core.Preferences;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Hosting;
using CAP.Avalonia.Services.AgentBridge.Registration;
using CAP.Avalonia.Services.Localization;
using ProcessLaunchFactory = CAP_Core.Export.ProcessLaunchFactory;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Settings.AgentBridge;

/// <summary>
/// Settings page "Agent bridge (MCP)": opt-in toggle, loopback port, the one-click
/// "Connect Claude Code" action, the optional access token and the copyable connect command,
/// plus a live status line.
/// </summary>
public partial class AgentBridgeSettingsViewModel : ObservableObject, IDisposable
{
    /// <summary>Lowest port offered (avoids privileged ports, which need admin rights on Unix).</summary>
    public const int MinPort = 1024;

    /// <summary>Highest TCP port.</summary>
    public const int MaxPort = 65535;

    private readonly IAgentBridgePreferences _preferences;
    private readonly AgentBridgeHost _host;
    private readonly ClaudeCodeConnector _connector;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private double _port;
    [ObservableProperty] private string _token;
    [ObservableProperty] private bool _requireToken;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isListening;
    [ObservableProperty] private string _feedbackText = "";
    [ObservableProperty] private bool _isFeedbackError;
    [ObservableProperty] private bool _isConnecting;

    /// <summary>Clipboard hook set by the view (clipboard access needs the top-level window).</summary>
    public Func<string, Task>? CopyToClipboard { get; set; }

    /// <summary>Creates the view-model and subscribes to server status changes.</summary>
    /// <param name="preferences">Persisted bridge settings.</param>
    /// <param name="host">The bridge server lifecycle.</param>
    /// <param name="connector">The one-click Claude Code registration; a default one using
    /// <see cref="ProcessLaunchFactory.CreateDefault"/> when null.</param>
    public AgentBridgeSettingsViewModel(
        IAgentBridgePreferences preferences, AgentBridgeHost host, ClaudeCodeConnector? connector = null)
    {
        _preferences = preferences;
        _host = host;
        _connector = connector
            ?? new ClaudeCodeConnector(preferences, host, new ClaudeCodeRegistrar(ProcessLaunchFactory.CreateDefault()));
        _isEnabled = preferences.GetAgentBridgeEnabled();
        _port = preferences.GetAgentBridgePort();
        _requireToken = preferences.GetAgentBridgeRequireToken();
        _token = preferences.GetOrCreateAgentBridgeToken();
        _host.StatusChanged += OnHostStatusChanged;
        RefreshStatus();
    }

    /// <summary>The command that registers this bridge with Claude Code (header only when a token is required).</summary>
    public string ConnectCommandText =>
        AgentBridgeHost.ClaudeCodeConnectCommand(SelectedPort, RequireToken ? Token : null);

    /// <summary>The MCP endpoint URL for the configured port.</summary>
    public string EndpointUrl => AgentBridgeHost.EndpointUrl(SelectedPort);

    private int SelectedPort => (int)Math.Clamp(Math.Round(Port), MinPort, MaxPort);

    partial void OnIsEnabledChanged(bool value)
    {
        _preferences.SetAgentBridgeEnabled(value);
        _ = ApplyAsync();
    }

    partial void OnPortChanged(double value)
    {
        OnPropertyChanged(nameof(ConnectCommandText));
        OnPropertyChanged(nameof(EndpointUrl));
    }

    partial void OnTokenChanged(string value) => OnPropertyChanged(nameof(ConnectCommandText));

    partial void OnRequireTokenChanged(bool value)
    {
        _preferences.SetAgentBridgeRequireToken(value);
        OnPropertyChanged(nameof(ConnectCommandText));
        ShowFeedback(Translate("Settings.AgentBridge.ReconnectNeeded"), isError: false);
    }

    /// <summary>
    /// Enables the bridge and registers it with Claude Code via its CLI. When the CLI is not
    /// installed, says so and leaves the copyable command as the fallback.
    /// </summary>
    [RelayCommand]
    private async Task ConnectClaudeCode()
    {
        IsConnecting = true;
        try
        {
            _preferences.SetAgentBridgePort(SelectedPort);
            var result = await _connector.ConnectAsync();
            IsEnabled = true;
            ShowFeedback(DescribeResult(result), isError: !result.Success);
        }
        finally
        {
            IsConnecting = false;
        }
    }

    /// <summary>Persists the port and restarts the server on it when enabled.</summary>
    [RelayCommand]
    private Task ApplyPort()
    {
        _preferences.SetAgentBridgePort(SelectedPort);
        Port = SelectedPort;
        return ApplyAsync();
    }

    /// <summary>Replaces the access token; agents must be reconnected with the new command.</summary>
    [RelayCommand]
    private void RegenerateToken()
    {
        Token = _preferences.RegenerateAgentBridgeToken();
        ShowFeedback(Translate("Settings.AgentBridge.TokenRegenerated"), isError: false);
    }

    /// <summary>Copies the Claude Code connect command (including the token header when required).</summary>
    [RelayCommand]
    private Task CopyConnectCommand() => CopyAsync(ConnectCommandText);

    /// <summary>Copies the bare access token.</summary>
    [RelayCommand]
    private Task CopyToken() => CopyAsync(Token);

    /// <summary>Unsubscribes from the host (the page is transient, the host a singleton).</summary>
    public void Dispose() => _host.StatusChanged -= OnHostStatusChanged;

    /// <summary>Localized one-line outcome of a registration attempt.</summary>
    public static string DescribeResult(ClaudeRegistrationResult result)
    {
        if (!result.CliFound)
            return Translate("Settings.AgentBridge.ClaudeNotFound");
        return result.Success
            ? Translate("Settings.AgentBridge.ClaudeConnected")
            : string.Format(Translate("Settings.AgentBridge.ClaudeFailed"), result.Output);
    }

    private static string Translate(string key) => LocalizationService.Instance.Translate(key);

    private void ShowFeedback(string text, bool isError)
    {
        IsFeedbackError = isError;
        FeedbackText = text;
    }

    private async Task CopyAsync(string text)
    {
        if (CopyToClipboard == null)
            return;
        await CopyToClipboard(text);
        ShowFeedback(Translate("Settings.AgentBridge.Copied"), isError: false);
    }

    private async Task ApplyAsync()
    {
        await _host.ApplySettingsAsync();
        RefreshStatus();
    }

    private void OnHostStatusChanged(object? sender, EventArgs e) => RefreshStatus();

    private void RefreshStatus()
    {
        IsListening = _host.IsRunning;
        if (_host.IsRunning)
            StatusText = string.Format(Translate("Settings.AgentBridge.StatusListening"),
                AgentBridgeHost.EndpointUrl(_host.Port));
        else if (IsEnabled && _host.LastError != null)
            StatusText = string.Format(Translate("Settings.AgentBridge.StatusFailed"),
                SelectedPort, _host.LastError);
        else
            StatusText = Translate("Settings.AgentBridge.StatusOff");
    }
}
