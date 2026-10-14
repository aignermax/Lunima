using CAP_Core.Routing;

namespace CAP_Core.LightCalculation.BendLoss;

/// <summary>
/// Computes the itemized insertion loss of a routed path — the single loss model shared by
/// routed connections, frozen group paths and the crossing-insertion cost comparison, so
/// grouping or re-routing a circuit never silently switches loss models.
/// <list type="bullet">
/// <item>Propagation: L_prop = α_prop[dB/cm] · length[µm] / 10⁴.</item>
/// <item>Bends: Σ L90(R) · |θ| / 90°, plus an optional flat excess per 90° (e.g. a measured
/// foundry figure the radius model does not capture).</item>
/// <item>Junctions: Σ J_ref · (Δκ · R_jref)² over every curvature change between consecutive
/// pieces; changes inside one sampled smooth curve are adiabatic and contribute their (small)
/// loss without counting as junctions.</item>
/// </list>
/// </summary>
public static class ConnectionLossCalculator
{
    private const double MicrometersPerCentimeter = 10_000.0;
    private const double DegreesPerQuarterTurn = 90.0;

    /// <summary>Curvature steps below this (1/µm, i.e. radii beyond 1 m) are numerically zero.</summary>
    private const double MinCurvatureStepPerMicrometer = 1e-6;

    /// <summary>
    /// Computes the loss breakdown of <paramref name="path"/>. A null or empty path yields
    /// <see cref="ConnectionLossBreakdown.Empty"/> (no loss assumed — conservative for a
    /// connection that has no geometry yet, matching the former behaviour).
    /// </summary>
    /// <param name="path">The routed geometry.</param>
    /// <param name="model">Bend-loss model of the waveguide cross-section.</param>
    /// <param name="propagationLossDbPerCm">Straight-waveguide propagation loss (dB/cm, ≥ 0).</param>
    /// <param name="excessBendLossDbPer90Deg">Optional flat extra loss per 90° of turn (dB, ≥ 0).</param>
    public static ConnectionLossBreakdown Compute(
        RoutedPath? path, BendLossModel model,
        double propagationLossDbPerCm, double excessBendLossDbPer90Deg = 0)
    {
        if (path?.Segments == null || path.Segments.Count == 0)
            return ConnectionLossBreakdown.Empty;

        double length = path.TotalLengthMicrometers;
        var pieces = CurvatureProfile.Build(path);
        var bends = CollectBends(pieces, model, Math.Max(0, excessBendLossDbPer90Deg));
        var (junctionDb, junctionCount) = SumJunctions(pieces, model);

        return new ConnectionLossBreakdown
        {
            PathLengthMicrometers = length,
            PropagationLossDb = Math.Min(BendLossModel.MaxLossDb,
                Math.Max(0, propagationLossDbPerCm) * length / MicrometersPerCentimeter),
            BendLossDb = Math.Min(BendLossModel.MaxLossDb, bends.Sum(b => b.LossDb)),
            JunctionLossDb = junctionDb,
            JunctionCount = junctionCount,
            Bends = bends,
            Family = model.Family,
        };
    }

    /// <summary>Arcs become one entry each; each run of sampled-curve vertices becomes one entry.</summary>
    private static List<BendLossEntry> CollectBends(
        IReadOnlyList<CurvaturePiece> pieces, BendLossModel model, double excessDbPer90)
    {
        var bends = new List<BendLossEntry>();
        BendLossEntry? run = null;
        foreach (var piece in pieces)
        {
            if (piece.Kind != CurvaturePieceKind.SampledCurve && run != null)
            {
                bends.Add(run.Value);
                run = null;
            }
            if (piece.Kind == CurvaturePieceKind.Straight)
                continue;

            double loss = model.RadiationLossDb(piece.RadiusMicrometers, piece.SweepDegrees)
                + excessDbPer90 * piece.SweepDegrees / DegreesPerQuarterTurn;
            var entry = new BendLossEntry(piece.RadiusMicrometers, piece.SweepDegrees, loss,
                piece.Kind == CurvaturePieceKind.SampledCurve);

            if (piece.Kind == CurvaturePieceKind.Arc)
                bends.Add(entry);
            else
                run = run == null ? entry : Merge(run.Value, entry);
        }
        if (run != null)
            bends.Add(run.Value);
        return bends;
    }

    private static BendLossEntry Merge(BendLossEntry run, BendLossEntry vertex) => run with
    {
        RadiusMicrometers = Math.Min(run.RadiusMicrometers, vertex.RadiusMicrometers),
        SweepDegrees = run.SweepDegrees + vertex.SweepDegrees,
        LossDb = run.LossDb + vertex.LossDb,
    };

    /// <summary>
    /// Sums the junction losses along the profile. The component ports at both ends are
    /// straight waveguides, so a path that starts or ends directly in a bend has a junction there.
    /// </summary>
    private static (double LossDb, int Count) SumJunctions(IReadOnlyList<CurvaturePiece> profile, BendLossModel model)
    {
        var portStraight = new CurvaturePiece(CurvaturePieceKind.Straight, 0, 0);
        var pieces = new List<CurvaturePiece>(profile.Count + 2) { portStraight };
        pieces.AddRange(profile);
        pieces.Add(portStraight);

        double loss = 0;
        int count = 0;
        for (int i = 1; i < pieces.Count; i++)
        {
            double step = Math.Abs(pieces[i].SignedCurvaturePerMicrometer - pieces[i - 1].SignedCurvaturePerMicrometer);
            if (double.IsNaN(step) || step < MinCurvatureStepPerMicrometer)
                continue;

            loss += model.JunctionLossDb(step);
            bool insideSampledCurve = pieces[i].Kind == CurvaturePieceKind.SampledCurve
                && pieces[i - 1].Kind == CurvaturePieceKind.SampledCurve;
            if (!insideSampledCurve)
                count++;
        }
        return (Math.Min(BendLossModel.MaxLossDb, loss), count);
    }
}
