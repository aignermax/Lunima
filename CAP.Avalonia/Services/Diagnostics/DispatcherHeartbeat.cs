using System.Diagnostics;
using Avalonia.Threading;

namespace CAP.Avalonia.Services.Diagnostics;

/// <summary>Measures queued dispatcher latency with at most one outstanding probe.</summary>
internal sealed class DispatcherHeartbeat
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(20);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _sampling;
    private readonly Action<Action> _post;
    private TimeSpan _maximum;

    internal DispatcherHeartbeat(Action<Action> post)
    {
        _post = post;
        _sampling = SampleAsync();
    }

    public static DispatcherHeartbeat? StartIfOnUiThread() =>
        SynchronizationContext.Current != null && Dispatcher.UIThread.CheckAccess()
            ? new DispatcherHeartbeat(action => Dispatcher.UIThread.Post(action, DispatcherPriority.Normal)) : null;

    public async Task<TimeSpan> CompleteAsync()
    {
        _stop.Cancel();
        // Cancel a pending probe even when shutdown has stopped the dispatcher pump.
        // The sampler records its elapsed latency, including the final continuation.
        try { await _sampling.ConfigureAwait(false); }
        finally { _stop.Dispose(); }
        return _maximum;
    }

    private async Task SampleAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var completion = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sent = Stopwatch.GetTimestamp();
            try { _post(() => completion.TrySetResult(Stopwatch.GetElapsedTime(sent))); }
            catch (Exception)
            {
                // Dispatcher shutdown must not turn optional diagnostics into a
                // command failure, or replace the command's original exception.
                return;
            }
            TimeSpan elapsed;
            try { elapsed = await completion.Task.WaitAsync(_stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                elapsed = Stopwatch.GetElapsedTime(sent);
                if (elapsed > _maximum) _maximum = elapsed;
                return;
            }
            if (elapsed > _maximum) _maximum = elapsed;
            try { await Task.Delay(Interval, _stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
        }
    }
}
