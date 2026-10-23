using System.Collections.Concurrent;
using System.Net.Sockets;

namespace CAP.Avalonia.Services.AgentBridge.Transport;

/// <summary>Owns one listener generation and its accepted connection tasks.</summary>
internal sealed class McpServerSession(TcpListener listener) : IDisposable
{
    private readonly ConcurrentDictionary<long, Task> _connections = new();
    private long _nextConnection;
    private volatile bool _stopping;
    internal bool IsStopping => _stopping;
    internal TcpListener Listener { get; } = listener;
    internal CancellationTokenSource Cancellation { get; } = new();
    internal Task AcceptLoop { get; set; } = Task.CompletedTask;

    internal void StopAccepting()
    {
        // Publish retirement before Stop invalidates TcpListener.Active. An
        // accept that already passed its loop condition can still enter later.
        _stopping = true;
        Listener.Stop();
    }

    internal void Track(Func<Task> serve)
    {
        var id = Interlocked.Increment(ref _nextConnection);
        var task = Task.Run(serve);
        _connections[id] = task;
        _ = task.ContinueWith(completed =>
        {
            _ = completed.Exception;
            _connections.TryRemove(id, out _);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    internal async Task DrainAsync()
    {
        try { await AcceptLoop.ConfigureAwait(false); }
        finally { await Task.WhenAll(_connections.Values).ConfigureAwait(false); }
    }

    public void Dispose() => Cancellation.Dispose();
}
