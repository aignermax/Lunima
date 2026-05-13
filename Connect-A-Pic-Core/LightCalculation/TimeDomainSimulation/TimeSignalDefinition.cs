namespace CAP_Core.LightCalculation.TimeDomainSimulation;

/// <summary>
/// Describes the time-domain input signal: sample rate, duration, and factory methods
/// for standard photonic pulse shapes (Gaussian, rectangular, chirp).
/// </summary>
public class TimeSignalDefinition
{
    private const double SpeedOfLightNmPerS = 2.998e17;

    /// <summary>Sample rate in Hz (= 1 / dt).</summary>
    public double SampleRateHz { get; }

    /// <summary>Number of time samples.</summary>
    public int NSamples { get; }

    /// <summary>Total duration in seconds = NSamples / SampleRateHz.</summary>
    public double DurationSeconds => NSamples / SampleRateHz;

    /// <summary>Time step dt = 1 / SampleRateHz in seconds.</summary>
    public double TimeStepSeconds => 1.0 / SampleRateHz;

    /// <summary>Time axis t[n] = n * dt in seconds.</summary>
    public double[] TimeAxis { get; }

    /// <summary>Initializes a new instance of <see cref="TimeSignalDefinition"/>.</summary>
    /// <param name="sampleRateHz">Sample rate in Hz.</param>
    /// <param name="nSamples">Number of time samples.</param>
    public TimeSignalDefinition(double sampleRateHz, int nSamples)
    {
        if (sampleRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        if (nSamples <= 0) throw new ArgumentOutOfRangeException(nameof(nSamples));

        SampleRateHz = sampleRateHz;
        NSamples = nSamples;
        TimeAxis = Enumerable.Range(0, nSamples).Select(i => i / sampleRateHz).ToArray();
    }

    /// <summary>
    /// Creates a <see cref="TimeSignalDefinition"/> whose sample rate and duration
    /// are derived from a wavelength sweep: dt = 1 / bandwidth, T = N * dt.
    /// </summary>
    /// <param name="centerWavelengthNm">Centre wavelength in nm.</param>
    /// <param name="spanNm">Full wavelength span in nm.</param>
    /// <param name="nFrequencyPoints">Number of frequency points (= number of time samples).</param>
    public static TimeSignalDefinition FromWavelengthSweep(
        double centerWavelengthNm, double spanNm, int nFrequencyPoints)
    {
        double fMax = SpeedOfLightNmPerS / (centerWavelengthNm - spanNm / 2.0);
        double fMin = SpeedOfLightNmPerS / (centerWavelengthNm + spanNm / 2.0);
        double bandwidth = fMax - fMin;
        return new TimeSignalDefinition(bandwidth, nFrequencyPoints);
    }

    /// <summary>Creates a Gaussian pulse centered at <paramref name="centerSeconds"/>.</summary>
    /// <param name="centerSeconds">Pulse centre in seconds.</param>
    /// <param name="sigmaSeconds">1-σ width in seconds.</param>
    /// <param name="amplitude">Peak amplitude (default 1.0).</param>
    public double[] CreateGaussianPulse(double centerSeconds, double sigmaSeconds, double amplitude = 1.0)
    {
        if (sigmaSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(sigmaSeconds));
        return TimeAxis.Select(t =>
            amplitude * Math.Exp(-0.5 * Math.Pow((t - centerSeconds) / sigmaSeconds, 2))).ToArray();
    }

    /// <summary>Creates a rectangular (top-hat) pulse.</summary>
    /// <param name="startSeconds">Pulse start in seconds.</param>
    /// <param name="endSeconds">Pulse end in seconds.</param>
    /// <param name="amplitude">Pulse amplitude (default 1.0).</param>
    public double[] CreateRectangularPulse(double startSeconds, double endSeconds, double amplitude = 1.0)
    {
        if (endSeconds <= startSeconds) throw new ArgumentException("endSeconds must be > startSeconds");
        return TimeAxis.Select(t => (t >= startSeconds && t <= endSeconds) ? amplitude : 0.0).ToArray();
    }
}
