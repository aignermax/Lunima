namespace CAP_Core.Solvers.ModeSolver;

/// <summary>
/// Result data for a single guided mode at a single wavelength.
/// </summary>
public class ModeSolverModeEntry
{
    /// <summary>Wavelength in µm at which this mode was computed.</summary>
    public double Wavelength { get; init; }

    /// <summary>Zero-based mode index (0 = fundamental, 1 = first higher-order, …).</summary>
    public int ModeIndex { get; init; }

    /// <summary>Effective refractive index n_eff = β / k₀.</summary>
    public double NEff { get; init; }

    /// <summary>
    /// Group index n_g = n_eff - λ · dn_eff/dλ.
    /// May equal <see cref="NEff"/> when the solver does not compute group index directly.
    /// </summary>
    public double NGroup { get; init; }

    /// <summary>
    /// Dominant polarisation of the mode ("TE", "TM", or "hybrid").
    /// </summary>
    public string Polarisation { get; init; } = "";

    /// <summary>
    /// Base-64-encoded PNG of the mode-field intensity profile, or <see langword="null"/>
    /// when the backend did not produce a field image.
    /// </summary>
    public string? ModeFieldPng { get; init; }
}
