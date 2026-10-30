namespace CAP_Core.Routing;

public partial class WaveguideRouter
{
    /// <summary>Default for <see cref="MaxRouteDuration"/>.</summary>
    public static readonly TimeSpan DefaultMaxRouteDuration = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Wall-clock budget of one <see cref="Route"/> call. The node budgets bound each search,
    /// but a wire with no path can run fine search, coarse retries and every breakout plan in
    /// turn and keep a routing pass busy for minutes on a dense chip. When the budget runs out
    /// the searches stop as if cancelled and the wire gets its blocked fallback, so the pass
    /// moves on; failing again at unchanged pins, it is remembered as unroutable and not
    /// searched again until something changes.
    /// </summary>
    public TimeSpan MaxRouteDuration { get; set; } = DefaultMaxRouteDuration;
}
