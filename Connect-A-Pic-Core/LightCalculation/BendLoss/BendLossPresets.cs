namespace CAP_Core.LightCalculation.BendLoss;

/// <summary>
/// Default bend-loss coefficients per waveguide family. Each set is a rounded, conservative
/// fit to representative published bend-loss curves; the derivation and the references are
/// documented in <c>docs/BEND-LOSS-MODEL.md</c>. All radii in µm, all losses in dB.
/// </summary>
public static class BendLossPresets
{
    /// <summary>
    /// Platform-agnostic default used when no PDK process is resolvable. A 90° bend at the
    /// 10 µm default radius loses 0.04 dB radiation + 2 × 0.005 dB junctions = 0.05 dB, the
    /// former constant, so legacy designs stay comparable; tighter bends lose far more
    /// (≈ 0.44 dB at 5 µm), gentle bends almost nothing (≈ 0.003 dB at 20 µm).
    /// </summary>
    public static BendLossModel Generic { get; } = new()
    {
        Family = WaveguideFamily.Generic,
        RadiationLossDbPer90AtReference = 0.04,
        RadiationReferenceRadiusMicrometers = 10.0,
        RadiationDecayLengthMicrometers = 2.17,
        CalibratedMinRadiusMicrometers = 5.0,
        JunctionLossDbAtReference = 0.005,
        JunctionReferenceRadiusMicrometers = 10.0,
        Source = "Generic (calibrated to the former 0.05 dB per 90° bend at R = 10 µm)",
    };

    /// <summary>SOI 220 nm strip: negligible radiation above ~3 µm; junctions dominate.</summary>
    public static BendLossModel SoiStrip { get; } = new()
    {
        Family = WaveguideFamily.SoiStrip,
        RadiationLossDbPer90AtReference = 0.056,
        RadiationReferenceRadiusMicrometers = 1.0,
        RadiationDecayLengthMicrometers = 0.43,
        CalibratedMinRadiusMicrometers = 1.0,
        JunctionLossDbAtReference = 0.015,
        JunctionReferenceRadiusMicrometers = 1.0,
        Source = "SOI 220 nm strip — fit to Vlasov & McNab, Opt. Express 12, 1622 (2004)",
    };

    /// <summary>Silicon nitride strip (~300 nm core): radiation significant below ~50 µm.</summary>
    public static BendLossModel SiliconNitride { get; } = new()
    {
        Family = WaveguideFamily.SiliconNitride,
        RadiationLossDbPer90AtReference = 0.05,
        RadiationReferenceRadiusMicrometers = 30.0,
        RadiationDecayLengthMicrometers = 8.7,
        CalibratedMinRadiusMicrometers = 20.0,
        JunctionLossDbAtReference = 0.01,
        JunctionReferenceRadiusMicrometers = 30.0,
        Source = "SiN strip — conservative fit, see docs/BEND-LOSS-MODEL.md",
    };

    /// <summary>Deep-etched InP: radii down to ~10 µm usable.</summary>
    public static BendLossModel InpDeepEtch { get; } = new()
    {
        Family = WaveguideFamily.InpDeepEtch,
        RadiationLossDbPer90AtReference = 0.05,
        RadiationReferenceRadiusMicrometers = 10.0,
        RadiationDecayLengthMicrometers = 4.3,
        CalibratedMinRadiusMicrometers = 10.0,
        JunctionLossDbAtReference = 0.02,
        JunctionReferenceRadiusMicrometers = 10.0,
        Source = "InP deep etch — conservative fit, see docs/BEND-LOSS-MODEL.md",
    };

    /// <summary>Shallow-etched InP rib: weak lateral confinement, radii of hundreds of µm.</summary>
    public static BendLossModel InpShallowRib { get; } = new()
    {
        Family = WaveguideFamily.InpShallowRib,
        RadiationLossDbPer90AtReference = 0.05,
        RadiationReferenceRadiusMicrometers = 150.0,
        RadiationDecayLengthMicrometers = 21.7,
        CalibratedMinRadiusMicrometers = 100.0,
        JunctionLossDbAtReference = 0.02,
        JunctionReferenceRadiusMicrometers = 150.0,
        Source = "InP shallow rib — conservative fit, see docs/BEND-LOSS-MODEL.md",
    };

    /// <summary>No bend loss (metal traces; isolating propagation loss in tests).</summary>
    public static BendLossModel None { get; } = new()
    {
        Family = WaveguideFamily.None,
        Source = "No bend loss",
    };

    /// <summary>The default coefficient set of <paramref name="family"/> (Generic for Custom).</summary>
    public static BendLossModel ForFamily(WaveguideFamily family) => family switch
    {
        WaveguideFamily.SoiStrip => SoiStrip,
        WaveguideFamily.SiliconNitride => SiliconNitride,
        WaveguideFamily.InpDeepEtch => InpDeepEtch,
        WaveguideFamily.InpShallowRib => InpShallowRib,
        WaveguideFamily.None => None,
        _ => Generic,
    };
}
