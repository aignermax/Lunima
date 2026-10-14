namespace CAP_Core.LightCalculation.BendLoss;

/// <summary>
/// One bend of a path and its radiation loss: a circular arc, or a whole sampled smooth curve
/// (S-bend/Cobra/Euler polyline) whose <see cref="RadiusMicrometers"/> is its tightest local radius.
/// </summary>
/// <param name="RadiusMicrometers">Arc radius, or the minimum local radius of a sampled curve (µm).</param>
/// <param name="SweepDegrees">Total absolute turn of the bend (degrees).</param>
/// <param name="LossDb">Radiation (plus any flat excess) loss of this bend (dB).</param>
/// <param name="IsSampledCurve">True for a polyline-sampled smooth curve rather than a circular arc.</param>
public readonly record struct BendLossEntry(
    double RadiusMicrometers, double SweepDegrees, double LossDb, bool IsSampledCurve);

/// <summary>
/// Itemized insertion loss of one waveguide connection or frozen path: propagation over the
/// path length, radiation in the bends, and mode-mismatch at curvature junctions. Every term
/// is ≥ 0 dB and the total is capped at <see cref="BendLossModel.MaxLossDb"/>, so
/// <see cref="AmplitudeTransmission"/> is always in (0, 1].
/// </summary>
public sealed record ConnectionLossBreakdown
{
    /// <summary>A zero-loss breakdown (no path / no pins).</summary>
    public static ConnectionLossBreakdown Empty { get; } = new();

    /// <summary>Geometric path length (µm).</summary>
    public double PathLengthMicrometers { get; init; }

    /// <summary>Propagation loss over the whole path length (dB).</summary>
    public double PropagationLossDb { get; init; }

    /// <summary>Radiation loss summed over all bends, including any flat per-90° excess (dB).</summary>
    public double BendLossDb { get; init; }

    /// <summary>Mode-mismatch loss summed over all curvature junctions (dB).</summary>
    public double JunctionLossDb { get; init; }

    /// <summary>Number of abrupt curvature junctions (straight↔bend, S-bend reversals).</summary>
    public int JunctionCount { get; init; }

    /// <summary>The bends of the path in order.</summary>
    public IReadOnlyList<BendLossEntry> Bends { get; init; } = Array.Empty<BendLossEntry>();

    /// <summary>Waveguide family of the model the breakdown was computed with.</summary>
    public WaveguideFamily Family { get; init; } = WaveguideFamily.Generic;

    /// <summary>Number of bends (arcs plus sampled smooth curves).</summary>
    public int BendCount => Bends.Count;

    /// <summary>Smallest bend radius on the path (µm), or null for a straight path.</summary>
    public double? MinBendRadiusMicrometers => Bends.Count == 0 ? null : Bends.Min(b => b.RadiusMicrometers);

    /// <summary>Largest bend radius on the path (µm), or null for a straight path.</summary>
    public double? MaxBendRadiusMicrometers => Bends.Count == 0 ? null : Bends.Max(b => b.RadiusMicrometers);

    /// <summary>Total insertion loss (dB), capped at <see cref="BendLossModel.MaxLossDb"/>.</summary>
    public double TotalLossDb =>
        Math.Min(BendLossModel.MaxLossDb, PropagationLossDb + BendLossDb + JunctionLossDb);

    /// <summary>Field amplitude transmission |t| = 10^(−TotalLossDb/20), always in (0, 1].</summary>
    public double AmplitudeTransmission => Math.Pow(10.0, -TotalLossDb / 20.0);
}
