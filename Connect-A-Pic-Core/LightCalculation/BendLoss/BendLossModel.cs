namespace CAP_Core.LightCalculation.BendLoss;

/// <summary>
/// Radius-dependent bend-loss model of one waveguide cross-section. Two physical terms:
/// <list type="bullet">
/// <item><b>Radiation loss</b> per 90° of turn, decaying exponentially with the bend radius R:
/// L90(R) = L_ref · exp(−(R − R_ref) / D)  [dB per 90°]. Equivalently the loss per unit arc
/// length is α(R) = L90(R) / (π·R/2)  [dB/µm]. A bend of sweep θ loses L90(R) · |θ| / 90°.</item>
/// <item><b>Junction (mode-mismatch) loss</b> wherever the curvature κ = 1/R changes abruptly
/// (straight↔bend, or an S-bend's direction reversal): the bent mode is shifted outwards
/// by an amount proportional to κ, so the overlap loss of a Gaussian-like mode grows with
/// the square of the curvature step: L_j = J_ref · (Δκ · R_jref)²  [dB].</item>
/// </list>
/// Below <see cref="CalibratedMinRadiusMicrometers"/> (the smallest radius the coefficients
/// were fitted at) the radiation term is extrapolated with half the decay length, i.e. it
/// rises twice as steeply — the conservative choice outside the calibrated range. Every
/// term is non-negative and the sum is capped at <see cref="MaxLossDb"/>, so a path can
/// never gain power (|t| ≤ 1) and never produces NaN or denormal amplitudes.
/// </summary>
public sealed record BendLossModel
{
    /// <summary>Upper bound (dB) for any single loss term and for a connection's total loss.</summary>
    public const double MaxLossDb = 300.0;

    /// <summary>Factor by which the decay length shrinks below the calibrated radius range.</summary>
    public const double ExtrapolationSteepening = 2.0;

    private const double DegreesPerQuarterTurn = 90.0;

    /// <summary>Platform the coefficients belong to (display and provenance only).</summary>
    public WaveguideFamily Family { get; init; } = WaveguideFamily.Generic;

    /// <summary>Radiation loss (dB per 90° of turn) at <see cref="RadiationReferenceRadiusMicrometers"/>.</summary>
    public double RadiationLossDbPer90AtReference { get; init; }

    /// <summary>Radius (µm) at which <see cref="RadiationLossDbPer90AtReference"/> applies.</summary>
    public double RadiationReferenceRadiusMicrometers { get; init; } = 1.0;

    /// <summary>Radius increase (µm) that reduces the radiation loss by a factor e.</summary>
    public double RadiationDecayLengthMicrometers { get; init; } = 1.0;

    /// <summary>Smallest radius (µm) the coefficients were fitted at; steeper extrapolation below.</summary>
    public double CalibratedMinRadiusMicrometers { get; init; }

    /// <summary>Loss (dB) of one straight↔bend junction at <see cref="JunctionReferenceRadiusMicrometers"/>.</summary>
    public double JunctionLossDbAtReference { get; init; }

    /// <summary>Bend radius (µm) at which <see cref="JunctionLossDbAtReference"/> applies.</summary>
    public double JunctionReferenceRadiusMicrometers { get; init; } = 1.0;

    /// <summary>Human-readable provenance of the coefficients (literature fit or PDK source).</summary>
    public string Source { get; init; } = "";

    /// <summary>
    /// Radiation loss (dB) per 90° of turn for a circular bend of <paramref name="radiusMicrometers"/>.
    /// Non-finite or non-positive radii (a corner without radius) return <see cref="MaxLossDb"/>.
    /// </summary>
    public double RadiationLossDbPer90Deg(double radiusMicrometers)
    {
        if (RadiationLossDbPer90AtReference <= 0 || RadiationDecayLengthMicrometers <= 0)
            return 0;
        if (!double.IsFinite(radiusMicrometers))
            return radiusMicrometers > 0 ? 0 : MaxLossDb;
        if (radiusMicrometers <= 0)
            return MaxLossDb;

        double decay = RadiationDecayLengthMicrometers;
        double exponent = radiusMicrometers >= CalibratedMinRadiusMicrometers
            ? -(radiusMicrometers - RadiationReferenceRadiusMicrometers) / decay
            : -(CalibratedMinRadiusMicrometers - RadiationReferenceRadiusMicrometers) / decay
              + (CalibratedMinRadiusMicrometers - radiusMicrometers) * ExtrapolationSteepening / decay;
        return CapExponential(RadiationLossDbPer90AtReference, exponent);
    }

    /// <summary>Radiation loss (dB) of a circular arc with the given radius and sweep (sign ignored).</summary>
    public double RadiationLossDb(double radiusMicrometers, double sweepDegrees) =>
        Math.Min(MaxLossDb, RadiationLossDbPer90Deg(radiusMicrometers) * Math.Abs(sweepDegrees) / DegreesPerQuarterTurn);

    /// <summary>
    /// Mode-mismatch loss (dB) of a junction where the curvature jumps by
    /// <paramref name="curvatureStepPerMicrometer"/> (|κ₁ − κ₂|, in 1/µm).
    /// </summary>
    public double JunctionLossDb(double curvatureStepPerMicrometer)
    {
        if (JunctionLossDbAtReference <= 0 || double.IsNaN(curvatureStepPerMicrometer))
            return 0;
        if (double.IsInfinity(curvatureStepPerMicrometer))
            return MaxLossDb;
        double normalized = Math.Abs(curvatureStepPerMicrometer) * JunctionReferenceRadiusMicrometers;
        return Math.Min(MaxLossDb, JunctionLossDbAtReference * normalized * normalized);
    }

    /// <summary>
    /// Total loss (dB) of an isolated straight–arc–straight corner: radiation over the arc plus
    /// its two junctions. Used by the router to compare candidate radii.
    /// </summary>
    public double CornerLossDb(double radiusMicrometers, double sweepDegrees)
    {
        if (radiusMicrometers <= 0)
            return MaxLossDb;
        double junctions = 2 * JunctionLossDb(1.0 / radiusMicrometers);
        return Math.Min(MaxLossDb, RadiationLossDb(radiusMicrometers, sweepDegrees) + junctions);
    }

    private static double CapExponential(double prefactor, double exponent)
    {
        double maxExponent = Math.Log(MaxLossDb / prefactor);
        return exponent >= maxExponent ? MaxLossDb : prefactor * Math.Exp(exponent);
    }
}
