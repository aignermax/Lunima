using Avalonia.Threading;

namespace CAP.Avalonia.Services.Diagnostics;

/// <summary>Measures queued dispatcher latency with at most one outstanding probe.</summary>
internal sealed class DispatcherHeartbeat
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(20);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _sampling;
    private readonly Action<Action> _post;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private DispatcherLatencySample? _pending;
    private bool _completed;
    private TimeSpan _maximum;

    internal DispatcherHeartbeat(Action<Action> post, TimeProvider? clock = null)
    {
        _post = post;
        _clock = clock ?? TimeProvider.System;
        _sampling = SampleAsync();
    }

    public static DispatcherHeartbeat? StartIfOnUiThread() =>
        SynchronizationContext.Current != null && Dispatcher.UIThread.CheckAccess()
            ? new DispatcherHeartbeat(action => Dispatcher.UIThread.Post(action, DispatcherPriority.Normal)) : null;

    public async Task<TimeSpan> CompleteAsync()
    {
        lock (_gate)
        {
            _completed = true;
            _pending?.Complete();
        }
        _stop.Cancel();
        // Cancel a pending probe even when shutdown has stopped the dispatcher pump.
        // Its interval ends here, before a delayed sampler continuation can inflate it.
        try { await _sampling.ConfigureAwait(false); }
        finally { _stop.Dispose(); }
        return _maximum;
    }

    private async Task SampleAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var completion = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
            DispatcherLatencySample sample;
            lock (_gate)
            {
                if (_completed) return;
                _pending = sample = new DispatcherLatencySample(_clock);
            }
            try
            {
                _post(() =>
                {
                    sample.Complete();
                    completion.TrySetResult(sample.Elapsed);
                });
            }
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
                elapsed = sample.Elapsed;
                if (elapsed > _maximum) _maximum = elapsed;
                return;
            }
            if (elapsed > _maximum) _maximum = elapsed;
            lock (_gate) _pending = null;
            try { await Task.Delay(Interval, _stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
        }
    }
}
