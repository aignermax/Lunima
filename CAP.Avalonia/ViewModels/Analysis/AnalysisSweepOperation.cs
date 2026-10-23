using CAP.Avalonia.Services.Lifetime;
using CAP_Core.Documents;

namespace CAP.Avalonia.ViewModels.Analysis;

/// <summary>Publication permission for one sweep, separate from its user-cancel feedback.</summary>
internal sealed class AnalysisSweepOperation : IDisposable
{
    private readonly ShellWorkScope _owner;
    private readonly Func<bool> _configurationCurrent;
    private readonly CancellationToken _caller;
    private readonly DocumentOperation _document;
    private readonly CancellationTokenSource _linked;
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
        _linked = CancellationTokenSource.CreateLinkedTokenSource(_document.CancellationToken, caller);
    }

    public CancellationToken Token => _linked.Token;
    public bool IsContextCurrent => !_disposed && !_owner.IsStopping && _configurationCurrent() && _document.IsCurrent;
    public bool CanPublish => IsContextCurrent && !_caller.IsCancellationRequested;

    public void ThrowIfObsolete()
    {
        _owner.ThrowIfStopping();
        _caller.ThrowIfCancellationRequested();
        if (!IsContextCurrent) throw new OperationCanceledException("The sweep context changed.", new CancellationToken(true));
    }

    public bool TryPublish(Action publish)
    {
        ThrowIfObsolete();
        return _document.TryPublish(publish);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _linked.Dispose();
        _document.Dispose();
    }
}
