using CAP.Avalonia.Services.Lifetime;
using CAP_Core.Documents;

namespace CAP.Avalonia.ViewModels.ComponentSettings;

/// <summary>Owns publication permission for one settings calculation, including a pending cost confirmation.</summary>
internal sealed class ComponentSettingsOperation : IDisposable
{
    private readonly ShellWorkScope _work;
    private readonly CancellationToken _caller;
    private readonly DocumentOperation? _document;
    private readonly CancellationTokenSource _cancellation;
    private bool _disposed;

    public ComponentSettingsOperation(ShellWorkScope work, DocumentSession? document,
        CancellationToken owner, CancellationToken caller = default)
    {
        _work = work;
        _caller = caller;
        work.ThrowIfStopping();
        // User cancellation stops a worker, but its still-current dialog may report
        // that cancellation. Only retirement/document changes revoke that context.
        _document = document?.CaptureOperation(owner);
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _document?.CancellationToken ?? owner, caller);
    }

    public CancellationToken Token => _cancellation.Token;
    public bool IsContextCurrent => !_disposed && !_work.IsStopping && (_document?.IsCurrent ?? true);
    public bool CanPublish(CancellationToken caller) =>
        IsContextCurrent && !_caller.IsCancellationRequested && !caller.IsCancellationRequested;

    public void ThrowIfObsolete(CancellationToken caller)
    {
        _work.ThrowIfStopping();
        _caller.ThrowIfCancellationRequested();
        caller.ThrowIfCancellationRequested();
        if (!IsContextCurrent)
            throw new OperationCanceledException("The component's document changed.", new CancellationToken(true));
    }

    public bool TryCommit(CancellationToken caller, Action commit)
    {
        ThrowIfObsolete(caller);
        if (_document != null) return _document.TryCommitAndAdvance(commit);
        commit();
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation.Dispose();
        _document?.Dispose();
    }
}
