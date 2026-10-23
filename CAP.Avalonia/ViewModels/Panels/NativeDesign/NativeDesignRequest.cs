using CAP_Contracts;
using CAP_Core.Documents;

namespace CAP.Avalonia.ViewModels.Panels.NativeDesign;

/// <summary>
/// Owns a native-file interaction from its first dialog until actual I/O cleanup.
/// An answer from a replaced document or superseded navigation cannot start more work.
/// </summary>
internal sealed class NativeDesignRequest : ICommitPermission, IDisposable, IAsyncDisposable
{
    private readonly DocumentOperation _operation;
    private readonly ICommitPermission? _caller;
    private readonly Func<bool>? _canProceed;

    private NativeDesignRequest(DocumentOperation operation, ICommitPermission? caller, Func<bool>? canProceed)
    {
        _operation = operation;
        _caller = caller;
        _canProceed = canProceed;
    }

    public CancellationToken CancellationToken => _operation.CancellationToken;
    public bool IsCurrent => TryCommit(static () => { });

    public static NativeDesignRequest? Begin(DocumentSession document, ICommitPermission? caller = null,
        Func<bool>? canProceed = null)
    {
        DocumentOperation? operation = null;
        void Capture()
        {
            if (canProceed?.Invoke() == false) return;
            operation = document.CaptureOperation(caller?.CancellationToken ?? default, scopeBound: false);
        }
        try
        {
            if (caller == null) Capture();
            else caller.Commit(Capture);
            return operation == null ? null : new NativeDesignRequest(operation, caller, canProceed);
        }
        catch (OperationCanceledException) { operation?.Dispose(); return null; }
    }

    public void Commit(Action change)
    {
        void Publish()
        {
            if (!_operation.TryPublish(() =>
                {
                    if (_canProceed?.Invoke() == false) throw new OperationCanceledException("Navigation was superseded.");
                    change();
                }))
                throw new OperationCanceledException("The file interaction no longer owns this document.");
        }
        if (_caller == null) Publish();
        else _caller.Commit(Publish);
    }

    public bool TryCommit(Action change)
    {
        var entered = false;
        try { Commit(() => { entered = true; change(); }); return true; }
        catch (OperationCanceledException) when (!entered) { return false; }
    }

    public void Dispose() => _operation.Dispose();
    public ValueTask DisposeAsync() => _operation.DisposeAsync();
}
