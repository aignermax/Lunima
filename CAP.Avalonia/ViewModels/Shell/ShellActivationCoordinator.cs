using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager;
using CAP.Avalonia.ViewModels.Settings.AgentBridge;
using CAP.Avalonia.ViewModels.Update;
using CAP_Core;

namespace CAP.Avalonia.ViewModels.Shell;

/// <summary>Explicit, once-only shell activation. Retirement cancels and drains its startup jobs.</summary>
internal sealed class ShellActivationCoordinator : IAsyncDisposable
{
    private readonly UpdateViewModel _updates;
    private readonly GdsExportViewModel _gds;
    private readonly ClaudeCodeConnectPromptViewModel? _prompt;
    private readonly PythonStartupViewModel? _python;
    private readonly ErrorConsoleService _errors;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationTokenSource _stop = new();
    private Task? _activation;
    private Task? _retirement;
    private bool _stopping;

    public ShellActivationCoordinator(UpdateViewModel updates, GdsExportViewModel gds,
        ClaudeCodeConnectPromptViewModel? prompt, PythonStartupViewModel? python, ErrorConsoleService errors,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        (_updates, _gds, _prompt, _python, _errors) = (updates, gds, prompt, python, errors);
        _delay = delay ?? Task.Delay;
    }

    // Like the view models it activates, this owner is called on the shell's UI context.
    public Task ActivateAsync(CancellationToken cancellationToken = default) =>
        _stopping ? Task.CompletedTask : _activation ??= RunAsync(cancellationToken);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        using var pythonCancellation = linked.Token.Register(() => _python?.Cancel());
        try
        {
            var environment = string.IsNullOrEmpty(_gds.CustomPythonPath)
                ? _gds.SearchForPythonAsync(linked.Token) : _gds.CheckEnvironmentAsync(linked.Token);
            await Task.WhenAll(environment, DelayedChecksAsync(linked.Token), _python?.StartOnceAsync() ?? Task.CompletedTask);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception error) { _errors.LogError("Startup checks failed.", error); }
    }

    private async Task DelayedChecksAsync(CancellationToken cancellationToken)
    {
        await _delay(TimeSpan.FromSeconds(2), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _updates.CheckForUpdatesOnStartupAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (_prompt != null) await _prompt.CheckOnStartupAsync(cancellationToken);
    }

    public Task StopAsync()
    {
        _stopping = true;
        return _retirement ??= RetireAsync();
    }

    private async Task RetireAsync()
    {
        try { await _stop.CancelAsync(); }
        catch (Exception error) { _errors.LogError("Startup cancellation failed.", error); }
        if (_python != null) await _python.StopAsync();
        if (_activation != null) await _activation;
        _stop.Dispose();
    }

    public void RequestStop() => _ = StopAsync();
    public ValueTask DisposeAsync() => new(StopAsync());
}
