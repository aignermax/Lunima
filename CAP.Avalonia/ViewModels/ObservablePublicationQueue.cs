using System.Runtime.ExceptionServices;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// Serializes synchronous display rebuilds. A nested rebuild waits until all subscribers
/// have received the current collection/property notification, preserving its index/item meaning.
/// Callers revoke private operation state immediately and guard queued actions with that state.
/// </summary>
internal sealed class ObservablePublicationQueue
{
    private readonly Queue<Action> _pending = new();
    private bool _publishing;

    /// <summary>Keeps an asynchronous operation alive until its queued display action really completes.</summary>
    internal Task PublishAsync(Action displayUpdate)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Publish(() =>
        {
            try { displayUpdate(); completion.TrySetResult(); }
            catch (Exception error) { completion.TrySetException(error); }
        });
        return completion.Task;
    }

    internal void Publish(Action displayUpdate)
    {
        ArgumentNullException.ThrowIfNull(displayUpdate);
        _pending.Enqueue(displayUpdate);
        if (_publishing) return;
        _publishing = true;
        List<Exception>? failures = null;
        try
        {
            while (_pending.TryDequeue(out var next))
            {
                try { next(); }
                catch (Exception error) { (failures ??= new()).Add(error); }
            }
        }
        finally { _publishing = false; }

        if (failures?.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures?.Count > 1) throw new AggregateException(failures);
    }
}
