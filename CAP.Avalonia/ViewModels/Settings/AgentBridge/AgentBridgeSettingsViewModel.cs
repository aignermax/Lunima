using CAP.Avalonia.Services.Lifetime;
using CAP_Core.Preferences;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Contracts;
using CAP.Avalonia.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Settings.AgentBridge;

/// <summary>
/// Settings page "Agent bridge (MCP)": opt-in toggle, loopback port, the one-click
/// "Connect Claude Code" action, the optional access token and the copyable connect command,
/// plus a live status line.
/// </summary>
public partial class AgentBridgeSettingsViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
    /// <summary>Lowest port offered (avoids privileged ports, which need admin rights on Unix).</summary>
    public const int MinPort = 1024;

    /// <summary>Highest TCP port.</summary>
    public const int MaxPort = 65535;

    private readonly IAgentBridgePreferences _preferences;
    private readonly IAgentBridgeControl _host;
    private readonly IAgentConnection _connector;
    private readonly ShellWorkScope _work = new();
    private AsyncCancellationScope? _connectionAttempt;
    private long _configurationVersion;
    private Task? _retirement;
    public Task StopAsync() => _retirement ??= RetireAsync();
    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task RetireAsync()
    {
        var stopped = _work.StopAsync();
        _host.StatusChanged -= OnHostStatusChanged;
        CopyToClipboard = null;
        await stopped;
    }

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
    /// <param name="connector">The configured client registration capability.</param>
    public AgentBridgeSettingsViewModel(
        IAgentBridgePreferences preferences, IAgentBridgeControl host, IAgentConnection connector,
        ApplicationWorkCoordinator? applicationWork = null)
    {
        _preferences = preferences;
        _host = host;
        _connector = connector;
        _work.RegisterWith(applicationWork, StopAsync);
        _isEnabled = preferences.GetAgentBridgeEnabled();
        _port = preferences.GetAgentBridgePort();
        _requireToken = preferences.GetAgentBridgeRequireToken();
        _token = preferences.GetOrCreateAgentBridgeToken();
        _host.StatusChanged += OnHostStatusChanged;
        RefreshStatus();
    }

    /// <summary>The command that registers this bridge with Claude Code (header only when a token is required).</summary>
    public string ConnectCommandText =>
        AgentBridgeAddress.ConnectCommand(SelectedPort, RequireToken ? Token : null);

    /// <summary>The MCP endpoint URL for the configured port.</summary>
    public string EndpointUrl => AgentBridgeAddress.EndpointUrl(SelectedPort);

    private int SelectedPort => (int)Math.Clamp(Math.Round(Port), MinPort, MaxPort);

    partial void OnIsEnabledChanged(bool value)
    {
        if (_work.IsStopping) return;
        InvalidateConnection();
        _preferences.SetAgentBridgeEnabled(value);
        _ = ApplyAsync();
    }

    partial void OnPortChanged(double value)
    {
        InvalidateConnection();
        OnPropertyChanged(nameof(ConnectCommandText));
        OnPropertyChanged(nameof(EndpointUrl));
    }

    partial void OnTokenChanged(string value)
    {
        InvalidateConnection();
        OnPropertyChanged(nameof(ConnectCommandText));
    }

    partial void OnRequireTokenChanged(bool value)
    {
        if (_work.IsStopping) return;
        InvalidateConnection();
        _preferences.SetAgentBridgeRequireToken(value);
        OnPropertyChanged(nameof(ConnectCommandText));
        ShowFeedback(Translate("Settings.AgentBridge.ReconnectNeeded"), isError: false);
    }

    /// <summary>
    /// Enables the bridge and registers it with Claude Code via its CLI. When the CLI is not
    /// installed, says so and leaves the copyable command as the fallback.
    /// </summary>
    [RelayCommand]
    private Task ConnectClaudeCode() => _work.RunAsync(ConnectCoreAsync);

    private async Task ConnectCoreAsync(CancellationToken stop)
    {
        var version = _configurationVersion;
        await using var attempt = new AsyncCancellationScope(stop);
        _connectionAttempt = attempt;
        IsConnecting = true;
        try
        {
            _preferences.SetAgentBridgePort(SelectedPort);
            var result = await _connector.ConnectAsync(attempt.Token);
            if (_work.IsStopping || version != _configurationVersion) return;
            // The accepted result may update the toggle without revoking its own completed request.
            _connectionAttempt = null;
            var enabledVersion = version + (IsEnabled ? 0 : 1);
            IsEnabled = true;
            // Synchronous toggle observers can change configuration or close this page.
            if (_work.IsStopping || _configurationVersion != enabledVersion) return;
            ShowFeedback(DescribeResult(result), isError: !result.Success);
        }
        catch (OperationCanceledException) when (_work.IsStopping || version != _configurationVersion) { }
        finally
        {
            if (ReferenceEquals(_connectionAttempt, attempt)) _connectionAttempt = null;
            IsConnecting = false;
        }
    }

    private void InvalidateConnection()
    {
        _configurationVersion++;
        _connectionAttempt?.Cancel();
    }

    /// <summary>Persists the port and restarts the server on it when enabled.</summary>
    [RelayCommand]
    private Task ApplyPort()
    {
        if (_work.IsStopping) return Task.CompletedTask;
        _preferences.SetAgentBridgePort(SelectedPort);
        Port = SelectedPort;
        return ApplyAsync();
    }

    /// <summary>Replaces the access token; agents must be reconnected with the new command.</summary>
    [RelayCommand]
    private void RegenerateToken()
    {
        if (_work.IsStopping) return;
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
    public void Dispose() => _ = StopAsync();

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

    private Task CopyAsync(string text) => _work.RunAsync(async _ =>
    {
        var copy = CopyToClipboard;
        if (copy == null) return;
        await copy(text);
        if (!_work.IsStopping) ShowFeedback(Translate("Settings.AgentBridge.Copied"), isError: false);
    });

    private Task ApplyAsync() => _work.RunAsync(async _ =>
    {
        await _host.ApplySettingsAsync();
        if (!_work.IsStopping) RefreshStatus();
    });

    private void OnHostStatusChanged(object? sender, EventArgs e) => RefreshStatus();

    private void RefreshStatus()
    {
        if (_work.IsStopping) return;
        IsListening = _host.IsRunning;
        if (_host.IsRunning)
            StatusText = string.Format(Translate("Settings.AgentBridge.StatusListening"),
                AgentBridgeAddress.EndpointUrl(_host.Port));
        else if (IsEnabled && _host.LastError != null)
            StatusText = string.Format(Translate("Settings.AgentBridge.StatusFailed"),
                SelectedPort, _host.LastError);
        else
            StatusText = Translate("Settings.AgentBridge.StatusOff");
    }
}
