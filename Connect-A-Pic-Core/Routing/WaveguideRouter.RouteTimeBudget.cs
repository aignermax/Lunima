namespace CAP_Core.Routing;

public partial class WaveguideRouter
{
    /// <summary>Default for <see cref="MaxRouteDuration"/>.</summary>
    public static readonly TimeSpan DefaultMaxRouteDuration = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The <see cref="MaxRouteDuration"/> every new router starts with. A wall-clock budget makes a
    /// route depend on machine speed, so code that needs reproducible routes (tests asserting exact
    /// outcomes) sets <see cref="Timeout.InfiniteTimeSpan"/> here once.
    /// </summary>
    public static TimeSpan MaxRouteDurationForNewRouters { get; set; } = DefaultMaxRouteDuration;

    /// <summary>
    /// Wall-clock budget of one <see cref="Route"/> call. The node budgets bound each search,
    /// but a wire with no path can run fine search, coarse retries and every breakout plan in
    /// turn and keep a routing pass busy for minutes on a dense chip. When the budget runs out
    /// the searches stop as if cancelled and the wire gets its blocked fallback, so the pass
    /// moves on; the fallback is flagged <see cref="RoutedPath.SearchTimedOut"/> and never
    /// remembered as unroutable, so a later pass tries the wire again.
    /// </summary>
    public TimeSpan MaxRouteDuration { get; set; } = MaxRouteDurationForNewRouters;
}
