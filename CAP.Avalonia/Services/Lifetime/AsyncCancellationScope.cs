namespace CAP.Avalonia.Services.Lifetime;

/// <summary>Combines cancellation without blocking the UI on downstream callbacks during disposal.</summary>
internal sealed class AsyncCancellationScope : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _source = new();
    private readonly CancellationTokenRegistration[] _registrations;
    private Task? _cancelling;
    private Task? _retirement;
    private bool _disposed;
    public CancellationToken Token { get; }

    public AsyncCancellationScope(params CancellationToken[] sources)
    {
        Token = _source.Token;
        _registrations = sources.Select(token => token.Register(Cancel)).ToArray();
    }

    public void Cancel()
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_disposed || _cancelling != null) return;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _cancelling = completion.Task;
        }
        _ = CancelCoreAsync(completion);
    }

    private async Task CancelCoreAsync(TaskCompletionSource completion)
    {
        try { await _source.CancelAsync().ConfigureAwait(false); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_retirement != null) return new(_retirement);
            _disposed = true;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _retirement = completion.Task;
        }
        _ = RetireAsync(completion);
        return new(completion.Task);
    }

    private async Task RetireAsync(TaskCompletionSource completion)
    {
        try
        {
            try
            {
                foreach (var registration in _registrations) await registration.DisposeAsync().ConfigureAwait(false);
                Task cancelling;
                lock (_gate) cancelling = _cancelling ?? Task.CompletedTask;
                await cancelling.ConfigureAwait(false);
            }
            finally { _source.Dispose(); }
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }
}
