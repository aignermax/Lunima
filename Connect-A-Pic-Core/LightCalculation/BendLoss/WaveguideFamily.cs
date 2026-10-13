namespace CAP_Core.LightCalculation.BendLoss;

/// <summary>
/// Waveguide platform a bend-loss coefficient set was fitted for. The family decides how
/// fast radiation loss grows as a bend tightens: high index contrast (SOI strip) confines
/// the mode so strongly that bends of a few micrometers are nearly lossless, while low
/// contrast platforms (silicon nitride, shallow-etched InP rib) leak light already at tens
/// to hundreds of micrometers.
/// </summary>
public enum WaveguideFamily
{
    /// <summary>
    /// Platform unknown (no PDK process resolvable). Coefficients are calibrated so a 90° bend
    /// at the 10 µm default radius reproduces the former constant 0.05 dB per bend.
    /// </summary>
    Generic,

    /// <summary>Silicon-on-insulator strip waveguide (220 nm core, ~500 nm wide).</summary>
    SoiStrip,

    /// <summary>Silicon nitride strip in oxide (moderate index contrast, e.g. 300 nm core).</summary>
    SiliconNitride,

    /// <summary>Deep-etched InP/InGaAsP waveguide (etched through the core).</summary>
    InpDeepEtch,

    /// <summary>Shallow-etched InP/InGaAsP rib waveguide (weak lateral confinement).</summary>
    InpShallowRib,

    /// <summary>Coefficients supplied explicitly by the PDK process cross-section.</summary>
    Custom,

    /// <summary>No bend loss at all (used for metal traces and for isolating other loss terms).</summary>
    None,
}
