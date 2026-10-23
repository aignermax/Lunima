namespace CAP.Avalonia.Services.Diagnostics;

/// <summary>Owns the time interval of one queued dispatcher probe.</summary>
internal sealed class DispatcherLatencySample
{
    private readonly TimeProvider _clock;
    private readonly long _started;
    private long _completedAt = long.MinValue;

    internal DispatcherLatencySample(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _started = _clock.GetTimestamp();
    }

    internal TimeSpan Elapsed
    {
        get
        {
            var completedAt = Volatile.Read(ref _completedAt);
            return _clock.GetElapsedTime(_started,
                completedAt == long.MinValue ? _clock.GetTimestamp() : completedAt);
        }
    }

    // A dispatcher acknowledgement or operation completion ends this same interval.
    internal void Complete() => Interlocked.CompareExchange(ref _completedAt, _clock.GetTimestamp(), long.MinValue);
}
