using CAP_Core.Routing;

namespace CAP_Core.LightCalculation.BendLoss;

/// <summary>How a <see cref="CurvaturePiece"/> was derived from the path geometry.</summary>
public enum CurvaturePieceKind
{
    /// <summary>A straight segment (zero curvature).</summary>
    Straight,

    /// <summary>A circular arc (<see cref="BendSegment"/>) with one explicit radius.</summary>
    Arc,

    /// <summary>
    /// A vertex of a sampled smooth curve (S-bend/Cobra polylines, imported Euler bends):
    /// the turn between two chords, with the radius of the circle through the chord ends.
    /// </summary>
    SampledCurve,
}

/// <summary>
/// One piece of a path's curvature profile, in path order.
/// </summary>
/// <param name="Kind">Origin of the piece.</param>
/// <param name="SignedCurvaturePerMicrometer">κ = ±1/R in 1/µm (positive = counter-clockwise), 0 for straights.</param>
/// <param name="SweepDegrees">Absolute turn of the piece in degrees (0 for straights).</param>
public readonly record struct CurvaturePiece(
    CurvaturePieceKind Kind, double SignedCurvaturePerMicrometer, double SweepDegrees)
{
    /// <summary>Bend radius in µm (∞ for straights).</summary>
    public double RadiusMicrometers => SignedCurvaturePerMicrometer == 0
        ? double.PositiveInfinity
        : 1.0 / Math.Abs(SignedCurvaturePerMicrometer);
}

/// <summary>
/// Converts a <see cref="RoutedPath"/> into its curvature profile — the sequence of straight,
/// arc and sampled-curve pieces the loss model integrates over. Arcs carry their exact radius.
/// A polyline of short chords (a sampled smooth curve) is treated as a curve whose local
/// radius at each vertex is min(chord₁, chord₂) / (2·sin(Δφ/2)); this integrates the
/// radiation loss over the varying curvature of Euler/sine bends. Chords strictly inside a
/// sampled curve are not emitted as straights, so consecutive vertex pieces form one smooth
/// curve and only their (small) curvature differences count as junctions — the adiabatic
/// transitions that make such curves low-loss. Known approximation: a hard kink between two
/// long straights is read as a large-radius turn (the router never produces such kinks).
/// </summary>
public static class CurvatureProfile
{
    /// <summary>Vertex turns below this (radians) are collinear joints, not curvature.</summary>
    private const double MinVertexTurnRadians = 1e-4;

    /// <summary>Straights shorter than this (µm) are degenerate and skipped.</summary>
    private const double MinSegmentLengthMicrometers = 1e-6;

    /// <summary>Builds the curvature profile of <paramref name="path"/> (empty for null/empty paths).</summary>
    public static IReadOnlyList<CurvaturePiece> Build(RoutedPath? path)
    {
        var pieces = new List<CurvaturePiece>();
        if (path?.Segments == null)
            return pieces;

        var segments = path.Segments
            .Where(s => s is BendSegment || s.LengthMicrometers > MinSegmentLengthMicrometers)
            .ToList();

        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i] is BendSegment bend)
            {
                pieces.Add(ArcPiece(bend));
                continue;
            }

            var startVertex = VertexAt(segments, i);
            var endVertex = VertexAt(segments, i + 1);
            if (startVertex == null || endVertex == null)
                pieces.Add(new CurvaturePiece(CurvaturePieceKind.Straight, 0, 0));
            if (endVertex != null)
                pieces.Add(endVertex.Value);
        }
        return pieces;
    }

    private static CurvaturePiece ArcPiece(BendSegment bend)
    {
        double radius = bend.RadiusMicrometers;
        double curvature = radius > 0 ? Math.Sign(bend.SweepAngleDegrees) / radius : double.PositiveInfinity;
        return new CurvaturePiece(CurvaturePieceKind.Arc, curvature, Math.Abs(bend.SweepAngleDegrees));
    }

    /// <summary>
    /// The sampled-curve vertex between segments[index − 1] and segments[index], or null when
    /// either neighbour is missing or not a straight, or the joint does not turn.
    /// </summary>
    private static CurvaturePiece? VertexAt(IReadOnlyList<PathSegment> segments, int index)
    {
        if (index <= 0 || index >= segments.Count)
            return null;
        if (segments[index - 1] is not StraightSegment previous || segments[index] is not StraightSegment current)
            return null;

        double turnRadians = SignedTurnRadians(Heading(previous), Heading(current));
        if (Math.Abs(turnRadians) < MinVertexTurnRadians)
            return null;

        double chord = Math.Min(previous.LengthMicrometers, current.LengthMicrometers);
        double radius = chord / (2.0 * Math.Sin(Math.Abs(turnRadians) / 2.0));
        double curvature = Math.Sign(turnRadians) / radius;
        return new CurvaturePiece(CurvaturePieceKind.SampledCurve, curvature, Math.Abs(turnRadians) * 180.0 / Math.PI);
    }

    private static double Heading(StraightSegment segment) =>
        Math.Atan2(segment.EndPoint.Y - segment.StartPoint.Y, segment.EndPoint.X - segment.StartPoint.X);

    private static double SignedTurnRadians(double from, double to)
    {
        double turn = (to - from) % (2 * Math.PI);
        if (turn > Math.PI) turn -= 2 * Math.PI;
        if (turn <= -Math.PI) turn += 2 * Math.PI;
        return turn;
    }
}
