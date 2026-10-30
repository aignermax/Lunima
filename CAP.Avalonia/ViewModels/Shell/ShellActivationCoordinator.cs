using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.Services.Lifetime;
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
    public Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        if (_stopping) return Task.CompletedTask;
        if (_activation != null) return _activation;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Startup changes observable properties before its first await. A reentrant
        // activation or retirement must already see the accepted job at that point.
        _activation = completion.Task;
        _ = CompleteAsync(completion, () => RunAsync(cancellationToken));
        return completion.Task;
    }

    private static async Task CompleteAsync(TaskCompletionSource completion, Func<Task> work)
    {
        try { await work(); completion.TrySetResult(); }
        catch (OperationCanceledException error) { completion.TrySetCanceled(error.CancellationToken); }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var linked = new AsyncCancellationScope(_stop.Token, cancellationToken);
        await using var pythonCancellation = linked.Token.Register(() => _python?.Cancel());
        try
        {
            var environment = string.IsNullOrEmpty(_gds.CustomPythonPath)
                ? _gds.SearchForPythonAsync(linked.Token) : _gds.CheckEnvironmentAsync(linked.Token);
            await Task.WhenAll(environment, DelayedChecksAsync(linked.Token), _python?.StartOnceAsync() ?? Task.CompletedTask);
        }
        catch (OperationCanceledException) when (linked.Token.IsCancellationRequested) { }
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
        if (_retirement != null) return _retirement;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _retirement = completion.Task;
        _ = CompleteAsync(completion, RetireAsync);
        return completion.Task;
    }

    private async Task RetireAsync()
    {
        try
        {
            // Start every retirement independently. One failing cancellation callback
            // must not prevent the remaining children from being stopped and drained.
            var all = Task.WhenAll(CancelStartupAsync(),
                _python == null ? Task.CompletedTask : StopChildAsync(_python.StopAsync),
                _prompt == null ? Task.CompletedTask : StopChildAsync(_prompt.StopAsync),
                _activation ?? Task.CompletedTask);
            try { await all; }
            catch when (all.Exception != null) { throw all.Exception; }
        }
        finally { _stop.Dispose(); }
    }

    private async Task CancelStartupAsync()
    {
        try { await _stop.CancelAsync(); }
        catch (Exception error) { _errors.LogError("Startup cancellation failed.", error); }
    }

    // Capture synchronous child failures as tasks so the other retirements still start.
    private static async Task StopChildAsync(Func<Task> stop) => await stop();

    public void RequestStop() => _ = StopAsync();
    public ValueTask DisposeAsync() => new(StopAsync());
}
