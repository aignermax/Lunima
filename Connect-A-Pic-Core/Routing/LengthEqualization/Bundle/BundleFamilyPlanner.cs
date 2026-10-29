namespace CAP_Core.Routing.LengthEqualization.Bundle;

/// <summary>
/// The planned shape of one bundle wire: a run from the near pin, a first jog starting at
/// <c>FirstJogU</c> that lifts the wire to <c>TopV</c>, a run there, and — when the wire
/// detours — a second jog starting at <c>SecondJogU</c> down to the far pin's line.
/// </summary>
internal sealed record BundleWireShape(BundleWire Wire, double TopV, double FirstJogU, double SecondJogU, double Radius)
{
    /// <summary>The first jog, from the near pin's line to the top line.</summary>
    public BundleJog FirstJog => new(TopV - Wire.StartV, Radius);

    /// <summary>The second jog, from the top line to the far pin's line.</summary>
    public BundleJog SecondJog => new(Wire.EndV - TopV, Radius);

    /// <summary>Total path length.</summary>
    public double Length => Wire.EndU - Wire.StartU + FirstJog.Excess + SecondJog.Excess;
}

/// <summary>
/// Lays out one family of a bundle — wires that all detour toward the same side — as nested
/// arches: the wire nearest the other family is innermost, each outer wire's top line lies at
/// least one pitch beyond its inner neighbour's, its first jog one pitch nearer the near pins
/// and its second jog one pitch nearer the far pins. Works in <c>w</c>, the across coordinate
/// pointing toward the family's detour side, so both families share the code.
/// </summary>
internal sealed class BundleFamilyPlanner
{
    private const double Slack = 1e-6;
    private readonly IReadOnlyList<BundleWire> _innerFirst;
    private readonly double _side;
    private readonly double _radius;
    private readonly double _pitch;
    private readonly double _lead;

    /// <param name="innerFirst">The family's wires, innermost first.</param>
    /// <param name="side">+1 when the family detours toward +v, -1 toward -v.</param>
    /// <param name="radius">Bend radius of every jog.</param>
    /// <param name="pitch">Centre-line distance between neighbouring legs and lines.</param>
    /// <param name="lead">Shortest straight kept at each pin before the first and after the last jog.</param>
    public BundleFamilyPlanner(IReadOnlyList<BundleWire> innerFirst, double side, double radius, double pitch, double lead)
    {
        (_innerFirst, _side, _radius, _pitch, _lead) = (innerFirst, side, radius, pitch, lead);
    }

    /// <summary>Longest wire of the family when each takes its shortest shape.</summary>
    public double ShortestCommonLength() =>
        _innerFirst.Count == 0 ? 0 : _innerFirst.Max(w => LengthAt(w, Base(w)));

    /// <summary>
    /// Top lines (in w) giving every wire <paramref name="target"/> where nesting allows; wires
    /// nesting pushes beyond it come out longer, which the caller resolves by raising the target.
    /// </summary>
    public double[] Tops(double target) => Nest(wire => TopFor(wire, target));

    /// <summary>Top lines giving every wire its shortest shape the nesting allows (no common length).</summary>
    public double[] ShortestTops() => Nest(Base);

    private double[] Nest(Func<BundleWire, double> wanted)
    {
        var tops = new double[_innerFirst.Count];
        for (int k = 0; k < tops.Length; k++)
        {
            var wire = _innerFirst[k];
            double top = wanted(wire);
            if (k > 0 && (Detours(wire, top) || Detours(_innerFirst[k - 1], tops[k - 1])))
                top = Math.Max(top, tops[k - 1] + _pitch);
            tops[k] = top;
        }
        return tops;
    }

    /// <summary>Length of the k-th wire (innermost first) with the given top line.</summary>
    public double LengthOf(int k, double top) => LengthAt(_innerFirst[k], top);

    /// <summary>
    /// Places the jogs along the axis for the given top lines; null when they do not fit
    /// between the pin columns.
    /// </summary>
    public List<BundleWireShape>? Shapes(double[] tops)
    {
        int n = _innerFirst.Count;
        var first = new double[n];
        var second = new double[n];
        double? outerStart = null, outerEnd = null, outerSecondStart = null, outerSecondEnd = null;
        for (int k = n - 1; k >= 0; k--)
        {
            var wire = _innerFirst[k];
            var (firstJog, secondJog) = (Jog(H(wire.StartV), tops[k]), Jog(H(wire.EndV), tops[k], fromTop: true));
            first[k] = Math.Max(wire.StartU + _lead,
                Math.Max((outerStart ?? double.MinValue) + _pitch, (outerEnd ?? double.MinValue) + _pitch - firstJog.Extent));
            if (firstJog.Extent > 0)
                (outerStart, outerEnd) = (first[k], first[k] + firstJog.Extent);
            second[k] = Math.Min(wire.EndU - _lead - secondJog.Extent,
                Math.Min((outerSecondStart ?? double.MaxValue) - _pitch, (outerSecondEnd ?? double.MaxValue) - _pitch - secondJog.Extent));
            if (secondJog.Extent > 0)
                (outerSecondStart, outerSecondEnd) = (second[k], second[k] + secondJog.Extent);
            double runEnd = secondJog.Extent > 0 ? second[k] : wire.EndU - _lead;
            if (first[k] + firstJog.Extent > runEnd + Slack)
                return null;
        }
        return _innerFirst.Select((w, k) => new BundleWireShape(w, _side * tops[k], first[k],
            Math.Max(second[k], first[k] + Jog(H(w.StartV), tops[k]).Extent), _radius)).ToList();
    }

    private double H(double v) => _side * v;

    private BundleJog Jog(double fromW, double top, bool fromTop = false) =>
        new(fromTop ? fromW - top : top - fromW, _radius);

    private double Base(BundleWire wire) => Math.Max(H(wire.StartV), H(wire.EndV));

    private bool Detours(BundleWire wire, double top) => top > H(wire.EndV) + Slack;

    private double LengthAt(BundleWire wire, double top) =>
        wire.EndU - wire.StartU + Jog(H(wire.StartV), top).Excess + Jog(H(wire.EndV), top).Excess;

    private double TopFor(BundleWire wire, double target)
    {
        double low = Base(wire);
        if (LengthAt(wire, low) >= target)
            return low;
        double high = low + target;
        for (int i = 0; i < 80; i++)
        {
            double mid = (low + high) / 2;
            if (LengthAt(wire, mid) < target)
                low = mid;
            else
                high = mid;
        }
        return (low + high) / 2;
    }
}
