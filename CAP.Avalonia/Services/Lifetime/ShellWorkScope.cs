namespace CAP.Avalonia.Services.Lifetime;

/// <summary>Owns cancellable shell jobs and retires only after their actual cleanup completes.</summary>
internal sealed class ShellWorkScope
{
    private readonly CancellationTokenSource _stop = new();
    private readonly HashSet<Task> _running = new();
    private Task? _retirement;
    private IDisposable? _applicationRegistration;

    // This owner and its callers resume on the shell synchronization context.
    public bool IsStopping { get; private set; }

    public void ThrowIfStopping()
    {
        // Linked token callbacks may still be queued behind another owner's cleanup.
        if (IsStopping) throw new OperationCanceledException("The feature is retiring.", new CancellationToken(true));
    }

    public void RegisterWith(ApplicationWorkCoordinator? application, Func<Task> retireFeature) =>
        _applicationRegistration = application?.Register(retireFeature);

    public Task RunAsync(Func<CancellationToken, Task> work)
    {
        if (IsStopping) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _running.Add(completion.Task);
        _ = ExecuteAsync(work, completion);
        return completion.Task;
    }

    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work)
    {
        if (IsStopping) return Task.FromCanceled<T>(new CancellationToken(true));
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _running.Add(completion.Task);
        _ = ExecuteAsync(work, completion);
        return completion.Task;
    }

    private async Task ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, TaskCompletionSource<T> completion)
    {
        try { completion.TrySetResult(await work(_stop.Token)); }
        catch (OperationCanceledException error) { completion.TrySetCanceled(error.CancellationToken); }
        catch (Exception error) { completion.TrySetException(error); }
        finally { _running.Remove(completion.Task); }
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> work, TaskCompletionSource completion)
    {
        try
        {
            await work(_stop.Token);
            completion.TrySetResult();
        }
        catch (OperationCanceledException error) { completion.TrySetCanceled(error.CancellationToken); }
        catch (Exception error) { completion.TrySetException(error); }
        finally { _running.Remove(completion.Task); }
    }

    public Task StopAsync()
    {
        IsStopping = true;
        return _retirement ??= RetireAsync();
    }

    private async Task RetireAsync()
    {
        var running = _running.ToArray();
        try
        {
            // Never mistake token cancellation for completed cleanup.
            var cancellation = _stop.CancelAsync();
            await Task.WhenAll(cancellation, DrainAsync(running));
        }
        finally
        {
            _applicationRegistration?.Dispose();
            _applicationRegistration = null;
            _stop.Dispose();
        }
    }

    private static async Task DrainAsync(Task[] running)
    {
        try { await Task.WhenAll(running); }
        catch (OperationCanceledException) { }
    }
}
