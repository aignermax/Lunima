namespace CAP_Core.Routing.LengthEqualization.Bundle;

/// <summary>
/// Plans a whole bundle at one common length. The wires (sorted across the axis) are split
/// into two families that detour away from each other — the lower ones toward -v, the upper
/// ones toward +v — at a point where the two families' extents do not overlap; each family is
/// laid out as nested arches. Without a requested length the common length is the shortest the
/// arches allow: the longest wire's shortest shape, raised only as far as the nesting forces.
/// </summary>
internal static class BundleLayout
{
    private const double LengthSlack = 1e-6;
    private const int MaxTargetRaises = 40;

    /// <summary>A planned bundle: every wire's shape at the common length.</summary>
    public sealed record Plan(double TargetLength, IReadOnlyList<BundleWireShape> Shapes);

    /// <summary>
    /// The plan with the shortest common length over all valid family splits; null when no split
    /// fits. A requested length that is too short for a split rules that split out.
    /// </summary>
    public static Plan? Build(IReadOnlyList<BundleWire> wires, double radius, double pitch, double lead, double? target)
    {
        Plan? best = null;
        for (int split = 0; split <= wires.Count; split++)
        {
            if (!Separated(wires, split, pitch))
                continue;
            var plan = BuildSplit(wires, split, radius, pitch, lead, target);
            if (plan != null && (best == null || plan.TargetLength < best.TargetLength - LengthSlack))
                best = plan;
        }
        return best;
    }

    /// <summary>
    /// The plan in which every wire takes its shortest planar shape — a river route without a
    /// common length — over the split with the least total length; null when no split fits.
    /// </summary>
    public static Plan? BuildShortest(IReadOnlyList<BundleWire> wires, double radius, double pitch, double lead)
    {
        Plan? best = null;
        double bestTotal = double.MaxValue;
        for (int split = 0; split <= wires.Count; split++)
        {
            if (!Separated(wires, split, pitch))
                continue;
            var (minus, plus) = Families(wires, split, radius, pitch, lead);
            var minusShapes = minus.Shapes(minus.ShortestTops());
            var plusShapes = plus.Shapes(plus.ShortestTops());
            if (minusShapes == null || plusShapes == null)
                continue;
            var shapes = minusShapes.Concat(plusShapes).ToList();
            double total = shapes.Sum(s => s.Length);
            if (total < bestTotal - LengthSlack)
                (best, bestTotal) = (new Plan(shapes.Max(s => s.Length), shapes), total);
        }
        return best;
    }

    private static (BundleFamilyPlanner Minus, BundleFamilyPlanner Plus) Families(IReadOnlyList<BundleWire> wires, int split,
        double radius, double pitch, double lead) => (
        new BundleFamilyPlanner(wires.Take(split).Reverse().ToList(), -1, radius, pitch, lead),
        new BundleFamilyPlanner(wires.Skip(split).ToList(), +1, radius, pitch, lead));

    /// <summary>The families may not reach into each other: the split wires' across-extents keep a pitch apart.</summary>
    private static bool Separated(IReadOnlyList<BundleWire> wires, int split, double pitch)
    {
        if (split == 0 || split == wires.Count)
            return true;
        var below = wires[split - 1];
        var above = wires[split];
        return Math.Max(below.StartV, below.EndV) + pitch <= Math.Min(above.StartV, above.EndV);
    }

    private static Plan? BuildSplit(IReadOnlyList<BundleWire> wires, int split, double radius, double pitch, double lead, double? target)
    {
        var (minus, plus) = Families(wires, split, radius, pitch, lead);
        double shortest = Math.Max(minus.ShortestCommonLength(), plus.ShortestCommonLength());
        if (target is { } requested && requested < shortest - LengthSlack)
            return null;
        double length = target ?? shortest;
        for (int raise = 0; raise < MaxTargetRaises; raise++)
        {
            var (minusTops, plusTops) = (minus.Tops(length), plus.Tops(length));
            double longest = Math.Max(Longest(minus, minusTops), Longest(plus, plusTops));
            if (longest > length + LengthSlack)
            {
                if (target != null)
                    return null;
                length = longest;
                continue;
            }
            var minusShapes = minus.Shapes(minusTops);
            var plusShapes = plus.Shapes(plusTops);
            if (minusShapes == null || plusShapes == null)
                return null;
            return new Plan(length, minusShapes.Concat(plusShapes).ToList());
        }
        return null;
    }

    private static double Longest(BundleFamilyPlanner family, double[] tops)
    {
        double longest = 0;
        for (int k = 0; k < tops.Length; k++)
            longest = Math.Max(longest, family.LengthOf(k, tops[k]));
        return longest;
    }
}
