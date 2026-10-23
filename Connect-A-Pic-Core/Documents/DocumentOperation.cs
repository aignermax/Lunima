namespace CAP_Core.Documents;

/// <summary>A revision-scoped operation. Dispose only after its worker and cleanup have finished.</summary>
public sealed class DocumentOperation : IDisposable, IAsyncDisposable
{
    private readonly DocumentSession _owner;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationTokenRegistration _callerCancellation;
    private readonly CancellationToken _callerToken;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;
    private Task? _cancelling;

    internal DocumentOperation(DocumentSession owner, Guid document, long revision, long? scopeRevision,
        CancellationToken cancellationToken)
    {
        _owner = owner;
        DocumentId = document;
        Revision = revision;
        ScopeRevision = scopeRevision;
        _callerToken = cancellationToken;
        _cancellation = new CancellationTokenSource();
        CancellationToken = _cancellation.Token;
        _callerCancellation = cancellationToken.Register(() => { _ = CancelAsync(); });
    }

    internal Guid DocumentId { get; }
    internal long Revision { get; set; }
    internal long? ScopeRevision { get; }
    internal Task Completion => _completion.Task;
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    internal bool IsCancellationRequested => _callerToken.IsCancellationRequested || CancellationToken.IsCancellationRequested;
    public CancellationToken CancellationToken { get; }
    public bool IsCurrent => _owner.IsCurrent(this);
    public bool TryPublish(Action publish) => _owner.TryPublish(this, publish);
    /// <summary>Atomically commits one computed batch and retains permission for its next batch.</summary>
    public bool TryCommitAndAdvance(Action commit) => _owner.TryCommitAndAdvance(this, commit);

    internal Task CancelAsync()
    {
        TaskCompletionSource completion;
        lock (_sync)
        {
            if (_cancelling != null) return _cancelling;
            if (IsDisposed) return Task.CompletedTask;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _cancelling = completion.Task;
        }
        // Neither callbacks nor their error reporting may acquire the session
        // lock while holding the operation lock (commits use the opposite order).
        _ = CancelAndObserveAsync(completion);
        return completion.Task;
    }

    private async Task CancelAndObserveAsync(TaskCompletionSource completion)
    {
        try
        {
            try { await _cancellation.CancelAsync().ConfigureAwait(false); }
            catch (AggregateException error) { _owner.RecordCancellationError(error); }
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

    public void Dispose()
    {
        Task cancellation;
        lock (_sync)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            cancellation = _cancelling ?? Task.CompletedTask;
        }
        _ = FinishAsync(cancellation);
    }

    /// <summary>Revokes admission and awaits cancellation callbacks and registration cleanup.</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return new ValueTask(Completion);
    }

    private async Task FinishAsync(Task cancellation)
    {
        try { await cancellation.ConfigureAwait(false); }
        catch (AggregateException) { /* The session records cancellation callback failures. */ }
        finally
        {
            _callerCancellation.Dispose();
            _cancellation.Dispose();
            _owner.Complete(this);
            _completion.TrySetResult();
        }
    }
}
