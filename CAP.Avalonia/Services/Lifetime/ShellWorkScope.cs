namespace CAP.Avalonia.Services.Lifetime;

/// <summary>Owns cancellable shell jobs and retires only after their actual cleanup completes.</summary>
internal sealed class ShellWorkScope
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly HashSet<Task> _running = new();
    private Task? _retirement;
    private IDisposable? _applicationRegistration;

    // Headless callers and asynchronous cleanup can resume without a UI synchronization context.
    public bool IsStopping { get { lock (_gate) return _retirement != null; } }

    public void ThrowIfStopping()
    {
        // Linked token callbacks may still be queued behind another owner's cleanup.
        if (IsStopping) throw new OperationCanceledException("The feature is retiring.", new CancellationToken(true));
    }

    public void RegisterWith(ApplicationWorkCoordinator? application, Func<Task> retireFeature)
    {
        ThrowIfStopping();
        var registration = application?.Register(retireFeature);
        IDisposable? release;
        bool stopping;
        lock (_gate)
        {
            stopping = _retirement != null;
            release = stopping ? registration : _applicationRegistration;
            if (!stopping) _applicationRegistration = registration;
        }
        release?.Dispose();
        if (stopping) ThrowIfStopping();
    }

    public Task RunAsync(Func<CancellationToken, Task> work)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token;
        lock (_gate)
        {
            if (_retirement != null) return Task.CompletedTask;
            token = _stop.Token;
            _running.Add(completion.Task);
        }
        // Preserve the caller's synchronous prefix and context; never invoke work under the state lock.
        _ = ExecuteAsync(work, completion, token);
        return completion.Task;
    }

    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token;
        lock (_gate)
        {
            if (_retirement != null) return Task.FromCanceled<T>(new CancellationToken(true));
            token = _stop.Token;
            _running.Add(completion.Task);
        }
        _ = ExecuteAsync(work, completion, token);
        return completion.Task;
    }

    private async Task ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, TaskCompletionSource<T> completion,
        CancellationToken token)
    {
        T result = default!;
        Exception? failure = null;
        try { result = await work(token); }
        catch (Exception error) { failure = error; }
        lock (_gate)
        {
            _running.Remove(completion.Task);
            // RunContinuationsAsynchronously prevents caller code from executing under the lock.
            if (failure is OperationCanceledException cancelled) completion.TrySetCanceled(cancelled.CancellationToken);
            else if (failure != null) completion.TrySetException(failure);
            else completion.TrySetResult(result);
        }
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> work, TaskCompletionSource completion,
        CancellationToken token)
    {
        Exception? failure = null;
        try { await work(token); }
        catch (Exception error) { failure = error; }
        lock (_gate)
        {
            _running.Remove(completion.Task);
            if (failure is OperationCanceledException cancelled) completion.TrySetCanceled(cancelled.CancellationToken);
            else if (failure != null) completion.TrySetException(failure);
            else completion.TrySetResult();
        }
    }

    public Task StopAsync()
    {
        TaskCompletionSource completion;
        Task[] running;
        IDisposable? registration;
        lock (_gate)
        {
            if (_retirement != null) return _retirement;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _retirement = completion.Task;
            running = _running.ToArray();
            registration = _applicationRegistration;
            _applicationRegistration = null;
        }
        _ = RetireAsync(running, registration, completion);
        return completion.Task;
    }

    private async Task RetireAsync(Task[] running, IDisposable? registration, TaskCompletionSource completion)
    {
        try
        {
            try
            {
                // Admission is already closed and the shared retirement task is visible
                // before cancellation callbacks can call back into this owner.
                var cancellation = _stop.CancelAsync();
                await Task.WhenAll(cancellation, DrainAsync(running));
            }
            finally
            {
                try { registration?.Dispose(); }
                finally { _stop.Dispose(); }
            }
            completion.TrySetResult();
        }
        catch (OperationCanceledException error) { completion.TrySetCanceled(error.CancellationToken); }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private static async Task DrainAsync(Task[] running)
    {
        try { await Task.WhenAll(running); }
        catch (OperationCanceledException) { }
    }
}
