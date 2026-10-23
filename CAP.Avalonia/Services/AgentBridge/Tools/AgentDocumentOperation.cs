using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Documents;

namespace CAP.Avalonia.Services.AgentBridge.Tools;

/// <summary>Owns one agent edit across commands and asynchronous document calculations.</summary>
public sealed class AgentDocumentOperation : IDisposable, IAsyncDisposable
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly AgentToolContext? _context;
    private readonly DocumentOperation _operation;

    public AgentDocumentOperation(DesignCanvasViewModel canvas, AgentToolContext? context = null)
    {
        _canvas = canvas;
        _context = context;
        DocumentOperation? operation = null;
        void Capture() => operation = canvas.Document.CaptureOperation(context?.CancellationToken ?? default);
        if (context == null) Capture(); else context.Commit(Capture);
        _operation = operation!;
    }

    /// <summary>Commits a command and rebases only its own synchronous document notifications.</summary>
    public void Commit(Action change)
    {
        void Publish()
        {
            using var suspended = _canvas.Routing.SuspendAutomaticRouting();
            if (!_operation.TryCommitAndAdvance(change)) throw Stale();
        }
        if (_context == null) Publish(); else _context.Commit(Publish);
    }

    public T Commit<T>(Func<T> change)
    {
        T result = default!;
        Commit(() => { result = change(); });
        return result;
    }

    /// <summary>Checks report/continuation permission without changing the document revision.</summary>
    public void EnsureCurrent()
    {
        void Check() { if (!_operation.IsCurrent) throw Stale(); }
        if (_context == null) Check(); else _context.Commit(Check);
    }

    /// <summary>Shares this operation with routing; cancellation still awaits the accepted worker.</summary>
    public async Task<bool> RouteAsync()
    {
        EnsureCurrent();
        await _canvas.Routing.RecalculateRoutesAsync(_operation, _context);
        EnsureCurrent();
        var complete = await Edit.RoutingAwaiter.WaitForIdleAsync(_canvas, cancellationToken: _operation.CancellationToken);
        EnsureCurrent();
        return complete;
    }

    private static OperationCanceledException Stale() => new("The agent edit no longer owns this document revision or scope.");
    public void Dispose() => _operation.Dispose();
    public ValueTask DisposeAsync() => _operation.DisposeAsync();
}
