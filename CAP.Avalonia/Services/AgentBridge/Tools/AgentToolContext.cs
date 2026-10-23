using CAP.Avalonia.Services.AgentBridge.Hosting;

namespace CAP.Avalonia.Services.AgentBridge.Tools;

/// <summary>Cancellation and revocable commit permission for one admitted tool call.</summary>
public sealed class AgentToolContext : IDisposable
{
    private readonly AgentOperationGate _owner;
    private readonly CancellationTokenSource _cancellation;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;
    internal long Generation { get; }
    internal Task Completion => _completion.Task;

    internal AgentToolContext(AgentOperationGate owner, long generation,
        CancellationToken lifetimeToken, CancellationToken requestToken)
    {
        _owner = owner;
        Generation = generation;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken, requestToken);
        CancellationToken = _cancellation.Token;
    }

    /// <summary>Cancelled when the caller cancels or bridge admission closes.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Rejects execution after cancellation, closure, or completion of this call.</summary>
    public void ThrowIfStopped() => _owner.Commit(this, static () => { });

    /// <summary>Runs a short synchronous mutation atomically with respect to admission closure.</summary>
    public void Commit(Action mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        _owner.Commit(this, mutation);
    }

    /// <summary>Releases the call's permission and signals that its accepted work has finished.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _owner.Complete(this);
        _cancellation.Dispose();
        _completion.TrySetResult();
    }
}
