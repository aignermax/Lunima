using CAP_Contracts;
using CAP_Core.Documents;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;

/// <summary>Read-only ownership of the live revision while private trials are in flight.</summary>
internal sealed class RepairTrialPermission : ICommitPermission, IAsyncDisposable
{
    private readonly AgentToolContext? _context;
    private readonly DocumentOperation _operation;
    internal DocumentRevisionStamp Stamp { get; }
    public CancellationToken CancellationToken => _operation.CancellationToken;

    internal RepairTrialPermission(AgentDesignSession session, AgentToolContext? context)
    {
        _context = context;
        DocumentOperation? operation = null;
        DocumentRevisionStamp stamp = default;
        void Capture()
        {
            stamp = session.Canvas.Document.CaptureRevision();
            operation = session.Canvas.Document.CaptureOperation(context?.CancellationToken ?? default);
        }
        if (context == null) Capture(); else context.Commit(Capture);
        _operation = operation!;
        Stamp = stamp;
    }

    public void Commit(Action action)
    {
        void Publish()
        {
            if (!_operation.TryPublish(action)) throw new OperationCanceledException("The repair input changed or its owner stopped.");
        }
        if (_context == null) Publish(); else _context.Commit(Publish);
    }

    internal void EnsureCurrent() => Commit(static () => { });
    public ValueTask DisposeAsync() => _operation.DisposeAsync();
}
