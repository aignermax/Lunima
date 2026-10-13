using System.Globalization;

namespace CAP_Core.Components.GroupPins;

/// <summary>
/// One pin about to become an external group pin: its position relative to the
/// group origin, its world-space angle and whether it is an electrical pin.
/// </summary>
/// <param name="RelativeX">X position relative to the group origin in µm.</param>
/// <param name="RelativeY">Y position relative to the group origin in µm (Y-down).</param>
/// <param name="AngleDegrees">World-space pin angle in degrees.</param>
/// <param name="IsElectrical">True for electrical (DC/RF) pins, which carry no light.</param>
public readonly record struct GroupPinCandidate(
    double RelativeX, double RelativeY, double AngleDegrees, bool IsElectrical);

/// <summary>
/// Names the external pins of a freshly created group by where they point instead of
/// the raw <c>&lt;component&gt;_&lt;pin&gt;</c> id: light pins pointing west become
/// <c>IN1, IN2…</c> (top to bottom), east <c>OUT1…</c> (top to bottom), north
/// <c>N1…</c> and south <c>S1…</c> (left to right); electrical pins become
/// <c>E1…</c> (left to right). Light enters a photonic circuit drawn left-to-right
/// from the west, so the names read like a gate's interface. Deterministic: equal
/// positions keep the input order, so the same pins always get the same names.
/// </summary>
public sealed class GroupPinNamer
{
    /// <summary>Prefix of light pins pointing west (gate inputs by convention).</summary>
    public const string WestPrefix = "IN";

    /// <summary>Prefix of light pins pointing east (gate outputs by convention).</summary>
    public const string EastPrefix = "OUT";

    /// <summary>Prefix of light pins pointing north.</summary>
    public const string NorthPrefix = "N";

    /// <summary>Prefix of light pins pointing south.</summary>
    public const string SouthPrefix = "S";

    /// <summary>Prefix of electrical pins.</summary>
    public const string ElectricalPrefix = "E";

    /// <summary>
    /// Returns one unique name per candidate, in the candidates' order.
    /// </summary>
    /// <param name="candidates">The pins to name.</param>
    public IReadOnlyList<string> AssignNames(IReadOnlyList<GroupPinCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var names = new string[candidates.Count];
        var indexed = candidates.Select((pin, index) => (Pin: pin, Index: index)).ToList();

        foreach (var family in indexed.GroupBy(entry => PrefixOf(entry.Pin)))
        {
            var ordered = OrderWithinFamily(family.Key, family);
            var number = 1;
            foreach (var entry in ordered)
                names[entry.Index] = family.Key + number++.ToString(CultureInfo.InvariantCulture);
        }
        return names;
    }

    /// <summary>The name family a pin belongs to.</summary>
    private static string PrefixOf(GroupPinCandidate pin)
    {
        if (pin.IsElectrical)
            return ElectricalPrefix;
        return PinFacingClassifier.Classify(pin.AngleDegrees) switch
        {
            PinFacing.West => WestPrefix,
            PinFacing.East => EastPrefix,
            PinFacing.North => NorthPrefix,
            _ => SouthPrefix,
        };
    }

    /// <summary>West/east pins count top to bottom; all others left to right.</summary>
    private static IEnumerable<(GroupPinCandidate Pin, int Index)> OrderWithinFamily(
        string prefix, IEnumerable<(GroupPinCandidate Pin, int Index)> family)
    {
        var countsVertically = prefix is WestPrefix or EastPrefix;
        return countsVertically
            ? family.OrderBy(e => e.Pin.RelativeY).ThenBy(e => e.Pin.RelativeX).ThenBy(e => e.Index)
            : family.OrderBy(e => e.Pin.RelativeX).ThenBy(e => e.Pin.RelativeY).ThenBy(e => e.Index);
    }
}
