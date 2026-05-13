using System.Numerics;

namespace CAP_Core.LightCalculation.MaterialDispersion;

/// <summary>
/// Interpolates S-parameter data across wavelengths using physically correct methods:
/// <list type="bullet">
///   <item>Magnitude: log-magnitude linear interpolation (avoids artefacts across resonances).</item>
///   <item>Phase: unwrap before interpolating, re-wrap after (prevents 359°→1° jump artefacts).</item>
/// </list>
/// When only one wavelength stop is available, the single stop is returned as-is.
/// When the requested wavelength is outside the defined range, the nearest endpoint is returned.
/// </summary>
public sealed class PhaseAwareInterpolator
{
    private const double MinMagnitude = 1e-12;

    /// <summary>
    /// Interpolates a set of S-parameter connections to produce values at an arbitrary wavelength.
    /// </summary>
    /// <param name="wavelengthStops">
    /// Ordered list of (wavelengthNm, connections) stops.
    /// Each connection is (fromPin, toPin, complex S-value).
    /// </param>
    /// <param name="targetWavelengthNm">The wavelength at which to interpolate.</param>
    /// <returns>
    /// Dictionary of (fromPin, toPin) → complex S-value at the target wavelength.
    /// </returns>
    public Dictionary<(string FromPin, string ToPin), Complex> Interpolate(
        IReadOnlyList<(double WavelengthNm, IReadOnlyList<(string FromPin, string ToPin, Complex Value)> Connections)> wavelengthStops,
        double targetWavelengthNm)
    {
        if (wavelengthStops == null || wavelengthStops.Count == 0)
            return new Dictionary<(string, string), Complex>();

        if (wavelengthStops.Count == 1)
            return ToDictionary(wavelengthStops[0].Connections);

        // Clamp to range
        if (targetWavelengthNm <= wavelengthStops[0].WavelengthNm)
            return ToDictionary(wavelengthStops[0].Connections);
        if (targetWavelengthNm >= wavelengthStops[^1].WavelengthNm)
            return ToDictionary(wavelengthStops[^1].Connections);

        // Find bracketing interval
        int lo = 0;
        while (lo < wavelengthStops.Count - 2 && wavelengthStops[lo + 1].WavelengthNm <= targetWavelengthNm)
            lo++;

        double wl0 = wavelengthStops[lo].WavelengthNm;
        double wl1 = wavelengthStops[lo + 1].WavelengthNm;
        double t = (targetWavelengthNm - wl0) / (wl1 - wl0);

        var dict0 = ToDictionary(wavelengthStops[lo].Connections);
        var dict1 = ToDictionary(wavelengthStops[lo + 1].Connections);

        var result = new Dictionary<(string, string), Complex>();

        // Interpolate all connections present in the lower stop
        foreach (var key in dict0.Keys)
        {
            Complex s0 = dict0[key];
            if (!dict1.TryGetValue(key, out Complex s1))
            {
                result[key] = s0;
                continue;
            }

            result[key] = InterpolateSParameter(s0, s1, t);
        }

        // Add connections only present in the upper stop (use t=1 effectively → s1)
        foreach (var key in dict1.Keys)
        {
            if (!result.ContainsKey(key))
                result[key] = dict1[key];
        }

        return result;
    }

    /// <summary>
    /// Interpolates a single complex S-parameter between two stops using
    /// log-magnitude linear interpolation and phase-unwrap interpolation.
    /// </summary>
    /// <param name="s0">S-parameter at the lower wavelength stop.</param>
    /// <param name="s1">S-parameter at the upper wavelength stop.</param>
    /// <param name="t">Interpolation fraction in [0,1], where 0 returns s0 and 1 returns s1.</param>
    public static Complex InterpolateSParameter(Complex s0, Complex s1, double t)
    {
        // Log-magnitude interpolation
        double mag0 = Math.Max(s0.Magnitude, MinMagnitude);
        double mag1 = Math.Max(s1.Magnitude, MinMagnitude);
        double logMag = (1 - t) * Math.Log(mag0) + t * Math.Log(mag1);
        double interpMag = Math.Exp(logMag);

        // Phase-unwrap interpolation
        double phase0 = s0.Phase;  // radians in (-π, π]
        double phase1 = s1.Phase;

        double phase1Unwrapped = UnwrapPhase(phase0, phase1);
        double interpPhase = (1 - t) * phase0 + t * phase1Unwrapped;

        return Complex.FromPolarCoordinates(interpMag, interpPhase);
    }

    /// <summary>
    /// Adjusts <paramref name="phase1"/> so that the jump from <paramref name="phase0"/>
    /// is within ±π radians (standard phase unwrapping for two-point case).
    /// </summary>
    private static double UnwrapPhase(double phase0, double phase1)
    {
        double diff = phase1 - phase0;
        // Wrap diff into (-π, π]
        while (diff > Math.PI) diff -= 2 * Math.PI;
        while (diff < -Math.PI) diff += 2 * Math.PI;
        return phase0 + diff;
    }

    private static Dictionary<(string, string), Complex> ToDictionary(
        IReadOnlyList<(string FromPin, string ToPin, Complex Value)> connections)
    {
        var dict = new Dictionary<(string, string), Complex>(connections.Count);
        foreach (var (from, to, value) in connections)
            dict[(from, to)] = value;
        return dict;
    }
}
