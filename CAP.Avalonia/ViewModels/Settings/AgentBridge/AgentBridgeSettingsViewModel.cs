using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Hosting;
using CAP.Avalonia.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Settings.AgentBridge;

/// <summary>
/// Settings page "Agent bridge (MCP)": opt-in toggle, loopback port, the per-install access
/// token and the ready-to-paste Claude Code connect command, plus a live status line.
/// </summary>
public partial class AgentBridgeSettingsViewModel : ObservableObject, IDisposable
{
    /// <summary>Lowest port offered (avoids privileged ports, which need admin rights on Unix).</summary>
    public const int MinPort = 1024;

    /// <summary>Highest TCP port.</summary>
    public const int MaxPort = 65535;

    private readonly UserPreferencesService _preferences;
    private readonly AgentBridgeHost _host;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private double _port;
    [ObservableProperty] private string _token;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isListening;
    [ObservableProperty] private string _feedbackText = "";

    /// <summary>Clipboard hook set by the view (clipboard access needs the top-level window).</summary>
    public Func<string, Task>? CopyToClipboard { get; set; }

    /// <summary>Creates the view-model and subscribes to server status changes.</summary>
    public AgentBridgeSettingsViewModel(UserPreferencesService preferences, AgentBridgeHost host)
    {
        _preferences = preferences;
        _host = host;
        _isEnabled = preferences.GetAgentBridgeEnabled();
        _port = preferences.GetAgentBridgePort();
        _token = preferences.GetOrCreateAgentBridgeToken();
        _host.StatusChanged += OnHostStatusChanged;
        RefreshStatus();
    }

    /// <summary>The command that registers this bridge with Claude Code.</summary>
    public string ConnectCommandText => AgentBridgeHost.ClaudeCodeConnectCommand(SelectedPort, Token);

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
        FeedbackText = LocalizationService.Instance.Translate("Settings.AgentBridge.TokenRegenerated");
    }

    /// <summary>Copies the Claude Code connect command (including the token header).</summary>
    [RelayCommand]
    private Task CopyConnectCommand() => CopyAsync(ConnectCommandText);

    /// <summary>Copies the bare access token.</summary>
    [RelayCommand]
    private Task CopyToken() => CopyAsync(Token);

    /// <summary>Unsubscribes from the host (the page is transient, the host a singleton).</summary>
    public void Dispose() => _host.StatusChanged -= OnHostStatusChanged;

    private async Task CopyAsync(string text)
    {
        if (CopyToClipboard == null)
            return;
        await CopyToClipboard(text);
        FeedbackText = LocalizationService.Instance.Translate("Settings.AgentBridge.Copied");
    }

    private async Task ApplyAsync()
    {
        await _host.ApplySettingsAsync();
        RefreshStatus();
    }

    private void OnHostStatusChanged(object? sender, EventArgs e) => RefreshStatus();

    private void RefreshStatus()
    {
        var localization = LocalizationService.Instance;
        IsListening = _host.IsRunning;
        if (_host.IsRunning)
            StatusText = string.Format(localization.Translate("Settings.AgentBridge.StatusListening"),
                AgentBridgeHost.EndpointUrl(_host.Port));
        else if (IsEnabled && _host.LastError != null)
            StatusText = string.Format(localization.Translate("Settings.AgentBridge.StatusFailed"),
                SelectedPort, _host.LastError);
        else
            StatusText = localization.Translate("Settings.AgentBridge.StatusOff");
    }
}
