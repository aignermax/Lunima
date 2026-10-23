using CAP.Avalonia.Services.Lifetime;
using CAP_Core.Documents;

namespace CAP.Avalonia.ViewModels.Analysis;

/// <summary>Publication permission for one sweep, separate from its user-cancel feedback.</summary>
internal sealed class AnalysisSweepOperation : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly ShellWorkScope _owner;
    private readonly Func<bool> _configurationCurrent;
    private readonly CancellationToken _caller;
    private readonly DocumentOperation _document;
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationTokenRegistration _documentRegistration;
    private readonly CancellationTokenRegistration _callerRegistration;
    private Task? _cancelling;
    private Task? _retirement;
    private volatile bool _cancellationRequested;
    private bool _disposed;

    public AnalysisSweepOperation(ShellWorkScope owner, DocumentSession document,
        Func<bool> configurationCurrent, CancellationToken ownerToken, CancellationToken caller,
        DocumentIdentity? identity = null)
    {
        _owner = owner;
        _configurationCurrent = configurationCurrent;
        _caller = caller;
        owner.ThrowIfStopping();
        _document = identity is { } expected
            ? document.CaptureOperation(expected, ownerToken)
            : document.CaptureOperation(ownerToken);
        _cancellation = new CancellationTokenSource();
        // Linked CTS disposal waits synchronously for its source registrations.
        // These callbacks only request async cancellation; disposal awaits both
        // their registrations and the actual downstream callback task explicitly.
        _documentRegistration = _document.CancellationToken.Register(Cancel);
        _callerRegistration = caller.Register(Cancel);
    }

    public CancellationToken Token => _cancellation.Token;
    public bool IsContextCurrent => !_disposed && !_owner.IsStopping && _configurationCurrent() && _document.IsCurrent;
    public bool CanPublish => IsContextCurrent && !_caller.IsCancellationRequested && !_cancellationRequested;

    public void ThrowIfObsolete()
    {
        _owner.ThrowIfStopping();
        _caller.ThrowIfCancellationRequested();
        if (_cancellationRequested) throw new OperationCanceledException(Token);
        if (!IsContextCurrent) throw new OperationCanceledException("The sweep context changed.", new CancellationToken(true));
    }

    public bool TryPublish(Action publish)
    {
        ThrowIfObsolete();
        return _document.TryPublish(publish);
    }

    /// <summary>Revokes publication immediately while scheduling callback work off the UI thread.</summary>
    public void Cancel()
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_disposed || _cancelling != null) return;
            _cancellationRequested = true;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _cancelling = completion.Task;
        }
        _ = CancelCoreAsync(completion);
    }

    private async Task CancelCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            await _cancellation.CancelAsync().ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_retirement != null) return new ValueTask(_retirement);
            _disposed = true;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _retirement = completion.Task;
        }
        _ = RetireAsync(completion);
        return new ValueTask(completion.Task);
    }

    private async Task RetireAsync(TaskCompletionSource completion)
    {
        try
        {
            try
            {
                await _documentRegistration.DisposeAsync().ConfigureAwait(false);
                await _callerRegistration.DisposeAsync().ConfigureAwait(false);
                Task cancelling;
                lock (_gate) cancelling = _cancelling ?? Task.CompletedTask;
                await cancelling.ConfigureAwait(false);
            }
            finally
            {
                // Keep the lease alive until the worker's cancellation observers
                // have drained, then await the document's own callback cleanup.
                try { await _document.DisposeAsync().ConfigureAwait(false); }
                finally { _cancellation.Dispose(); }
            }
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }
}
