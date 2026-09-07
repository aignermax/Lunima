namespace UnitTests.Regression;

/// <summary>Outcome of one golden-gate run: any gate violations plus the measured data.</summary>
public sealed class GoldenGateResult
{
    /// <summary>Gate violations (load/route/DRC/pin-resolution). Empty means the gate passed.</summary>
    public List<string> Violations { get; } = new();

    /// <summary>Sweep wavelengths actually simulated (nm).</summary>
    public int[] WavelengthsNm { get; set; } = Array.Empty<int>();

    /// <summary>Measured linear transmission per output pin reference.</summary>
    public Dictionary<string, double[]> Transmission { get; } = new();

    /// <summary>Measured linear transmission per truth-table row, keyed by pin reference.</summary>
    public List<Dictionary<string, double>> TruthActuals { get; } = new();
}
