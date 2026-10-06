namespace CAP_Core.Routing.ImportedGeometry;

/// <summary>
/// A ribbon polygon split at its two end caps: <see cref="SideA"/> and
/// <see cref="SideB"/> both run from cap 1 to cap 2, so <c>SideA[i]</c> and
/// <c>SideB[i]</c> face each other across the ribbon at the ends.
/// </summary>
/// <param name="SideA">First long side, from cap 1 to cap 2.</param>
/// <param name="SideB">Second long side, from cap 1 to cap 2.</param>
public sealed record RibbonSides(IReadOnlyList<(double X, double Y)> SideA, IReadOnlyList<(double X, double Y)> SideB)
{
    /// <summary>Two consecutive right-angle turns sum to 180°; caps need at least this much turning.</summary>
    private const double MinCapTurnDegrees = 120.0;

    /// <summary>Points closer than this (µm) are treated as duplicates.</summary>
    private const double DuplicateToleranceUm = 1e-6;

    /// <summary>Midpoint of cap 1.</summary>
    public (double X, double Y) CapStartMidpoint => Mid(SideA[0], SideB[0]);

    /// <summary>Midpoint of cap 2.</summary>
    public (double X, double Y) CapEndMidpoint => Mid(SideA[^1], SideB[^1]);

    /// <summary>Length of cap 1 (µm).</summary>
    public double CapStartLength => Distance(SideA[0], SideB[0]);

    /// <summary>Length of cap 2 (µm).</summary>
    public double CapEndLength => Distance(SideA[^1], SideB[^1]);

    /// <summary>
    /// Splits a closed ribbon outline at its caps. The caps are the two
    /// non-adjacent edges with the most turning at their end vertices (a cap is
    /// flanked by two ~90° corners); ties go to the shorter edge. Returns null when
    /// the outline has fewer than four distinct vertices or no edge pair turns like
    /// a pair of caps.
    /// </summary>
    /// <param name="outline">Polygon vertices; a repeated closing vertex is ignored.</param>
    public static RibbonSides? Split(IReadOnlyList<(double X, double Y)> outline)
    {
        var points = Deduplicate(outline);
        int n = points.Count;
        if (n < 4) return null;

        var turn = new double[n];
        for (int i = 0; i < n; i++)
            turn[i] = TurnDegrees(points[(i - 1 + n) % n], points[i], points[(i + 1) % n]);

        var ranked = Enumerable.Range(0, n)
            .Select(i => (Edge: i, Score: turn[i] + turn[(i + 1) % n], Length: Distance(points[i], points[(i + 1) % n])))
            .OrderByDescending(e => Math.Round(e.Score, 3))
            .ThenBy(e => e.Length)
            .ToList();
        var first = ranked[0];
        var second = ranked.Skip(1).FirstOrDefault(e => !AreAdjacent(e.Edge, first.Edge, n));
        if (second.Score < MinCapTurnDegrees || first.Score < MinCapTurnDegrees) return null;

        int capA = Math.Min(first.Edge, second.Edge), capB = Math.Max(first.Edge, second.Edge);
        var sideA = Walk(points, capA + 1, capB);
        var sideB = Walk(points, capB + 1, capA + n);
        sideB.Reverse();
        return new RibbonSides(sideA, sideB);
    }

    private static List<(double X, double Y)> Walk(List<(double X, double Y)> points, int from, int to)
    {
        var result = new List<(double X, double Y)>();
        for (int i = from; i <= to; i++)
            result.Add(points[i % points.Count]);
        return result;
    }

    private static bool AreAdjacent(int a, int b, int n) =>
        a == b || (a + 1) % n == b || (b + 1) % n == a;

    private static List<(double X, double Y)> Deduplicate(IReadOnlyList<(double X, double Y)> outline)
    {
        var result = new List<(double X, double Y)>(outline.Count);
        foreach (var p in outline)
        {
            if (result.Count == 0 || Distance(result[^1], p) > DuplicateToleranceUm)
                result.Add(p);
        }
        while (result.Count > 1 && Distance(result[0], result[^1]) <= DuplicateToleranceUm)
            result.RemoveAt(result.Count - 1);
        return result;
    }

    /// <summary>Absolute direction change (degrees) at <paramref name="b"/> walking a→b→c.</summary>
    private static double TurnDegrees((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
    {
        double a1 = Math.Atan2(b.Y - a.Y, b.X - a.X), a2 = Math.Atan2(c.Y - b.Y, c.X - b.X);
        double d = Math.Abs(a2 - a1) * 180.0 / Math.PI;
        return d > 180.0 ? 360.0 - d : d;
    }

    internal static (double X, double Y) Mid((double X, double Y) a, (double X, double Y) b) =>
        ((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);

    internal static double Distance((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
