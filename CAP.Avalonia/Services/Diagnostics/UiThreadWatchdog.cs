using System.Diagnostics;

namespace CAP.Avalonia.Services.Diagnostics;

/// <summary>
/// Times a command invocation on the calling thread and reports a stall when the
/// UI-blocking portion exceeds the 100 ms responsiveness budget from CLAUDE.md §5
/// (issue #1150). The harness wraps each <c>[RelayCommand]</c> body in
/// <see cref="Measure(string, Action)"/> or <see cref="MeasureAsync(string, Func{Task})"/>;
/// over-budget measurements surface via <see cref="StallDetected"/> with the command
/// name and stall duration so tests can fail with an actionable message.
/// </summary>
public static class UiThreadWatchdog
{
    /// <summary>Responsiveness budget in milliseconds (CLAUDE.md §5).</summary>
    public const long BudgetMs = 100;

    /// <summary>
    /// Fires once per measured command whose UI-blocking portion exceeded
    /// <see cref="BudgetMs"/>. Handler exceptions are swallowed so a broken
    /// subscriber can never corrupt the measured command.
    /// </summary>
    public static event Action<UiStallMeasurement>? StallDetected;

    /// <summary>
    /// Runs <paramref name="body"/> and reports a stall if it takes longer than
    /// <see cref="BudgetMs"/>. Returns the elapsed milliseconds so callers can also
    /// assert on the measurement directly.
    /// </summary>
    public static long Measure(string commandName, Action body)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            body();
        }
        finally
        {
            Report(commandName, Stopwatch.GetElapsedTime(start));
        }
        return ElapsedMs(start);
    }

    /// <summary>
    /// Measures the synchronous prefix and dispatcher responsiveness until completion,
    /// including continuations after an await. Background waiting is not a UI stall.
    /// Returns full wall-clock duration and preserves the command's exception.
    /// </summary>
    public static async Task<long> MeasureAsync(string commandName, Func<Task> body)
    {
        var start = Stopwatch.GetTimestamp();
        var heartbeat = DispatcherHeartbeat.StartIfOnUiThread();
        var prefix = TimeSpan.Zero;
        try
        {
            Task task;
            try { task = body(); }
            finally { prefix = Stopwatch.GetElapsedTime(start); }
            await task;
        }
        finally
        {
            var maximum = heartbeat == null ? TimeSpan.Zero : await heartbeat.CompleteAsync();
            Report(commandName, prefix > maximum ? prefix : maximum);
        }
        return ElapsedMs(start);
    }

    private static long ElapsedMs(long startTimestamp) =>
        (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

    private static void Report(string commandName, TimeSpan elapsed)
    {
        if (elapsed.TotalMilliseconds <= BudgetMs)
            return;
        try
        {
            StallDetected?.Invoke(new UiStallMeasurement(commandName, (long)elapsed.TotalMilliseconds));
        }
        catch
        {
            // A faulty subscriber must never break the measured command.
        }
    }
}

/// <summary>
/// One over-budget measurement: the name of the offending command and how long it
/// blocked the UI thread, in milliseconds.
/// </summary>
public readonly record struct UiStallMeasurement(string CommandName, long StallMs)
{
    /// <summary>Formats the measurement for assertion messages and log lines.</summary>
    public override string ToString() => $"{CommandName} → {StallMs} ms";
}
