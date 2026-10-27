using CAP.Avalonia.Services.Lifetime;
using CAP_Core.Preferences;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Contracts;
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
public partial class ClaudeCodeConnectPromptViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IAgentConnectionPromptPreferences _preferences;
    private readonly IAgentConnection _connector;
    private readonly string _appVersion;
    private readonly ShellWorkScope _work = new();
    public Task StopAsync() => _work.StopAsync();
    public ValueTask DisposeAsync() => new(StopAsync());

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private string _resultText = "";
    [ObservableProperty] private bool _isDone;

    /// <summary>Creates the prompt (hidden until <see cref="CheckOnStartupAsync"/> decides to show it).</summary>
    public ClaudeCodeConnectPromptViewModel(
        IAgentConnectionPromptPreferences preferences, IAgentConnection connector, string appVersion,
        ApplicationWorkCoordinator? applicationWork = null)
    {
        _preferences = preferences;
        _connector = connector;
        _appVersion = appVersion;
        _work.RegisterWith(applicationWork, StopAsync);
    }

    /// <summary>
    /// Shows the bar when Claude Code is installed, Lunima is not registered with it, and the
    /// user has not answered "Not now" for this version. Runs the CLI off the UI thread.
    /// </summary>
    public Task CheckOnStartupAsync(CancellationToken cancellationToken = default) => _work.RunAsync(async stop =>
    {
        if (_preferences.GetClaudeCodePromptDismissedVersion() == _appVersion) return;
        cancellationToken.ThrowIfCancellationRequested();
        await using var cancellation = new AsyncCancellationScope(stop, cancellationToken);
        try
        {
            var registered = await Task.Run(() => _connector.IsRegisteredAsync(cancellation.Token), cancellation.Token);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_work.IsStopping) IsVisible = registered == false;
        }
        catch (OperationCanceledException) when (_work.IsStopping) { }
    });

    /// <summary>Enables the bridge and registers it with Claude Code.</summary>
    [RelayCommand]
    private Task Connect() => _work.RunAsync(ConnectCoreAsync);

    private async Task ConnectCoreAsync(CancellationToken stop)
    {
        IsConnecting = true;
        try
        {
            var result = await _connector.ConnectAsync(stop);
            if (_work.IsStopping) return;
            ResultText = AgentBridgeSettingsViewModel.DescribeResult(result);
            IsDone = result.Success;
            if (!result.CliFound)
                ResultText += " " + _connector.ManualCommand;
        }
        catch (OperationCanceledException) when (_work.IsStopping) { }
        finally
        {
            IsConnecting = false;
        }
    }

    /// <summary>Hides the bar until the next app version.</summary>
    [RelayCommand]
    private void NotNow()
    {
        if (_work.IsStopping) return;
        _preferences.SetClaudeCodePromptDismissedVersion(_appVersion);
        IsVisible = false;
        _ = StopAsync();
    }

    /// <summary>Closes the bar after a finished connect (the result stays in Settings).</summary>
    [RelayCommand]
    private void Close()
    {
        IsVisible = false;
        _ = StopAsync();
    }
}
