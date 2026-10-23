using CAP.Avalonia.Services.AgentBridge.Tools;

namespace CAP.Avalonia.Services.AgentBridge.Hosting;

/// <summary>Owns admission and mutation permission for one bridge server generation.</summary>
public sealed class AgentOperationGate
{
    private readonly object _sync = new();
    private readonly HashSet<AgentToolContext> _active = new();
    private CancellationTokenSource _lifetime = new();
    private bool _accepting = true;
    private long _generation;
    private Task? _closing;

    /// <summary>Callback failure from the last cancellation, separate from successful draining.</summary>
    public AggregateException? CancellationError { get; private set; }

    /// <summary>Opens a new generation after the previous generation has drained.</summary>
    public void Open()
    {
        lock (_sync)
        {
            if (_active.Count != 0 || _closing is { IsCompleted: false })
                throw new InvalidOperationException("The previous agent operations have not drained.");
            _lifetime.Dispose();
            _lifetime = new CancellationTokenSource();
            _generation++;
            _accepting = true;
            _closing = null;
            CancellationError = null;
        }
    }

    /// <summary>Revokes all mutation permission immediately, then awaits accepted operations.</summary>
    public Task CloseAsync()
    {
        CancellationTokenSource lifetime;
        Task drained;
        TaskCompletionSource completion;
        lock (_sync)
        {
            if (_closing != null) return _closing;
            _accepting = false;
            lifetime = _lifetime;
            drained = Task.WhenAll(_active.Select(context => context.Completion));
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _closing = completion.Task;
        }
        _ = Task.Run(() => CancelAndDrainAsync(lifetime, drained, completion));
        return completion.Task;
    }

    private async Task CancelAndDrainAsync(CancellationTokenSource lifetime,
        Task drained, TaskCompletionSource completion)
    {
        try
        {
            try { await lifetime.CancelAsync().ConfigureAwait(false); }
            catch (AggregateException error) { CancellationError = error; }
            finally { await drained.ConfigureAwait(false); }
            completion.SetResult();
        }
        catch (Exception error) { completion.SetException(error); }
    }

    internal AgentToolContext Enter(CancellationToken requestToken)
    {
        lock (_sync)
        {
            if (!_accepting) throw new OperationCanceledException("Agent bridge admission is closed.");
            requestToken.ThrowIfCancellationRequested();
            var context = new AgentToolContext(this, _generation, _lifetime.Token, requestToken);
            _active.Add(context);
            return context;
        }
    }

    internal void Commit(AgentToolContext context, Action mutation)
    {
        lock (_sync)
        {
            if (!_accepting || context.Generation != _generation || !_active.Contains(context))
                throw new OperationCanceledException("The agent operation no longer owns mutation permission.");
            context.CancellationToken.ThrowIfCancellationRequested();
            mutation();
        }
    }

    internal void Complete(AgentToolContext context)
    {
        lock (_sync) _active.Remove(context);
    }
}
