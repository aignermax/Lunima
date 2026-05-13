namespace CAP_Core.LightCalculation.TimeDomainSimulation;

/// <summary>
/// Result of a time-domain (transient) simulation.
/// Contains per-output-pin intensity traces and the shared time axis.
/// </summary>
public class TimeDomainResult
{
    /// <summary>
    /// Shared time axis in seconds: t[n] = n * dt for n = 0 … N-1.
    /// All traces share this axis.
    /// </summary>
    public double[] TimeAxis { get; }

    /// <summary>
    /// Per-output-pin intensity trace |E(t)|² in (field units)².
    /// Key = outflow pin Guid; Value = array with Length == TimeAxis.Length.
    /// </summary>
    public Dictionary<Guid, double[]> PinTraces { get; }

    /// <summary>Initializes a new instance of <see cref="TimeDomainResult"/>.</summary>
    /// <param name="timeAxis">Time axis shared by all traces (seconds).</param>
    /// <param name="pinTraces">Per-pin intensity traces.</param>
    public TimeDomainResult(double[] timeAxis, Dictionary<Guid, double[]> pinTraces)
    {
        TimeAxis = timeAxis ?? throw new ArgumentNullException(nameof(timeAxis));
        PinTraces = pinTraces ?? throw new ArgumentNullException(nameof(pinTraces));
    }
}
