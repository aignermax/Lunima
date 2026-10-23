namespace CAP_Core.Analysis.MeasuredSpectrum;

/// <summary>Converts optical power units independently of any spectrum file format.</summary>
public static class PowerConversion
{
    /// <summary>Converts dB to linear power: 10^(dB/10).</summary>
    public static double DecibelToLinear(double decibel) => Math.Pow(10.0, decibel / 10.0);
}
