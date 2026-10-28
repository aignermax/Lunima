namespace CAP_Core.Export.Contracts;

/// <summary>Result of a GDS coordinate comparison run.</summary>
public class GdsCoordinateComparisonResult
{
    /// <summary>True if all elements are within tolerance.</summary>
    public bool Passed { get; init; }

    /// <summary>Maximum centroid deviation across all elements in µm.</summary>
    public double MaxDeviationUm { get; init; }

    /// <summary>Root-mean-square centroid deviation in µm.</summary>
    public double RmsDeviationUm { get; init; }

    /// <summary>Tolerance used for the comparison in µm.</summary>
    public double ToleranceUm { get; init; }

    /// <summary>Full human-readable report from the Python script stdout.</summary>
    public string RawOutput { get; init; } = string.Empty;

    /// <summary>Error text from stderr (populated on failure).</summary>
    public string ErrorOutput { get; init; } = string.Empty;

    /// <summary>
    /// Formatted status line for UI display, e.g. "PASS — max Δ 0.000001 µm".
    /// </summary>
    public string StatusText => Passed
        ? $"PASS — max \u0394 {MaxDeviationUm:F6} \u03bcm"
        : $"FAIL — max \u0394 {MaxDeviationUm:F6} \u03bcm";
}
