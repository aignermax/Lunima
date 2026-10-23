using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Documents;

namespace CAP.Avalonia.Services.AgentBridge.Tools;

/// <summary>Owns one agent edit across commands and asynchronous document calculations.</summary>
public sealed class AgentDocumentOperation : IDisposable, IAsyncDisposable
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly AgentToolContext? _context;
    private readonly DocumentOperation _operation;
    private readonly List<Task> _derivedWork = new();
    private readonly CancellationTokenSource _stopping;
    private Task? _retirement;

    public AgentDocumentOperation(DesignCanvasViewModel canvas, AgentToolContext? context = null)
    {
        _canvas = canvas;
        _context = context;
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(context?.CancellationToken ?? default);
        DocumentOperation? operation = null;
        void Capture() => operation = canvas.Document.CaptureOperation(_stopping.Token);
        try { if (context == null) Capture(); else context.Commit(Capture); }
        catch { _stopping.Dispose(); throw; }
        _operation = operation!;
    }

    /// <summary>Commits a command and rebases only its own synchronous document notifications.</summary>
    public void Commit(Action change)
    {
        // This outer scope ends after both admission gates. Nested command scopes may
        // request bounds, but cannot capture a half-committed document or orphan its work.
        var bounds = _canvas.Dies.DeferScopeBoundsUpdates(_operation, _context);
        void Publish()
        {
            using var suspended = _canvas.Routing.SuspendAutomaticRouting();
            if (!_operation.TryCommitAndAdvance(change)) throw Stale();
        }
        try { if (_context == null) Publish(); else _context.Commit(Publish); }
        finally
        {
            bounds.Dispose();
            if (!bounds.Completion.IsCompletedSuccessfully) _derivedWork.Add(bounds.Completion);
        }
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
        await CompleteDerivedWorkAsync();
        EnsureCurrent();
        var routing = _canvas.Routing.RecalculateRoutesAsync(_operation, _context);
        _derivedWork.Add(routing);
        await routing;
        EnsureCurrent();
        var complete = await Edit.RoutingAwaiter.WaitForIdleAsync(_canvas, cancellationToken: _operation.CancellationToken);
        EnsureCurrent();
        return complete;
    }

    /// <summary>Waits for accepted derived grid work without searching or changing restored routes.</summary>
    public async Task CompleteDerivedWorkAsync()
    {
        await Task.WhenAll(_derivedWork);
        EnsureCurrent();
    }

    private static OperationCanceledException Stale() => new("The agent edit no longer owns this document revision or scope.");
    public void Dispose() => _ = RetireAsync();
    public ValueTask DisposeAsync() => new(RetireAsync());

    private Task RetireAsync() => _retirement ??= RetireCoreAsync();

    private async Task RetireCoreAsync()
    {
        // Keep document admission until accepted workers AND cancellation callbacks finish.
        // CancelAsync never executes arbitrary registrations synchronously on the UI thread.
        try { await Task.WhenAll(_stopping.CancelAsync(), Task.WhenAll(_derivedWork)); }
        finally
        {
            try { await _operation.DisposeAsync(); }
            finally { _stopping.Dispose(); }
        }
    }
}
