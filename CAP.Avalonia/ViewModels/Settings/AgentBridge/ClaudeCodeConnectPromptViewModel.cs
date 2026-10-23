using CAP_Core.Preferences;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Registration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Settings.AgentBridge;

/// <summary>
/// The dismissible main-window info bar "Connect Claude Code": offered once per app version
/// when the Claude Code CLI is installed but Lunima is not registered with it yet — the path
/// for the macOS .dmg and the Linux portable build, which have no install step, and the
/// fallback when the Windows installer could not register. Never registers silently: one
/// click on "Connect Claude Code" enables the bridge and registers it; "Not now" hides the
/// bar until the next version.
/// </summary>
public partial class ClaudeCodeConnectPromptViewModel : ObservableObject
{
    private readonly IAgentConnectionPromptPreferences _preferences;
    private readonly ClaudeCodeConnector _connector;
    private readonly string _appVersion;

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private string _resultText = "";
    [ObservableProperty] private bool _isDone;

    /// <summary>Creates the prompt (hidden until <see cref="CheckOnStartupAsync"/> decides to show it).</summary>
    public ClaudeCodeConnectPromptViewModel(
        IAgentConnectionPromptPreferences preferences, ClaudeCodeConnector connector, string appVersion)
    {
        _preferences = preferences;
        _connector = connector;
        _appVersion = appVersion;
    }

    /// <summary>
    /// Shows the bar when Claude Code is installed, Lunima is not registered with it, and the
    /// user has not answered "Not now" for this version. Runs the CLI off the UI thread.
    /// </summary>
    public async Task CheckOnStartupAsync(CancellationToken cancellationToken = default)
    {
        if (_preferences.GetClaudeCodePromptDismissedVersion() == _appVersion)
            return;
        var registered = await Task.Run(() => _connector.Registrar.IsRegisteredAsync(cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        IsVisible = registered == false;
    }

    /// <summary>Enables the bridge and registers it with Claude Code.</summary>
    [RelayCommand]
    private async Task Connect()
    {
        IsConnecting = true;
        try
        {
            var result = await _connector.ConnectAsync();
            ResultText = AgentBridgeSettingsViewModel.DescribeResult(result);
            IsDone = result.Success;
            if (!result.CliFound)
                ResultText += " " + _connector.ManualCommand;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    /// <summary>Hides the bar until the next app version.</summary>
    [RelayCommand]
    private void NotNow()
    {
        _preferences.SetClaudeCodePromptDismissedVersion(_appVersion);
        IsVisible = false;
    }

    /// <summary>Closes the bar after a finished connect (the result stays in Settings).</summary>
    [RelayCommand]
    private void Close() => IsVisible = false;
}
