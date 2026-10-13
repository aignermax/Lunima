using Component = CAP_Core.Components.Core.Component;

namespace CAP_Core.Analysis.Hierarchy;

/// <summary>
/// Decides whether two children of the SAME group whose bounding boxes overlap merely
/// graze through their padded cell margins. A group's internal layout is authored and
/// frozen deliberately, and PDK cells carry cladding margin around their waveguide core:
/// two 10 µm-tall straight-waveguide cells fanning into an MMI at its 8 µm (or 4 µm)
/// port pitch overlap by 2 µm (or 6 µm) of margin while their cores stay apart — the
/// waveguide overlap/spacing rules cover the cores. A pin is where a cell's waveguide
/// core meets its boundary, so a sibling overlap is a real clash only when a port of one
/// cell is buried inside the other's footprint, or when the two are stacked on top of
/// each other (a duplicate pasted in place, a child dropped onto another).
/// </summary>
public static class SiblingGrazeRule
{
    /// <summary>Fraction of the smaller footprint, per axis, from which two siblings count as stacked.</summary>
    public const double StackedOverlapFraction = 0.9;

    /// <summary>
    /// True when <paramref name="a"/> and <paramref name="b"/> share their immediate parent
    /// group, neither buries a port inside the other, and they are not stacked.
    /// </summary>
    /// <param name="a">First component.</param>
    /// <param name="b">Second component.</param>
    /// <param name="overlap">The overlap rectangle of the two footprints.</param>
    public static bool IsGraze(
        Component a, Component b, (double MinX, double MinY, double MaxX, double MaxY) overlap)
    {
        if (a.ParentGroup is null || !ReferenceEquals(a.ParentGroup, b.ParentGroup))
            return false;
        if (IsStacked(a, b, overlap))
            return false;
        return !BuriesPinOf(a, inside: b) && !BuriesPinOf(b, inside: a);
    }

    private static bool IsStacked(
        Component a, Component b, (double MinX, double MinY, double MaxX, double MaxY) overlap)
    {
        double smallerWidth = Math.Min(a.WidthMicrometers, b.WidthMicrometers);
        double smallerHeight = Math.Min(a.HeightMicrometers, b.HeightMicrometers);
        return overlap.MaxX - overlap.MinX >= smallerWidth * StackedOverlapFraction
            && overlap.MaxY - overlap.MinY >= smallerHeight * StackedOverlapFraction;
    }

    /// <summary>
    /// True when a pin of <paramref name="owner"/> lies inside the footprint of
    /// <paramref name="inside"/>, deeper than the touching tolerance from its edges.
    /// </summary>
    private static bool BuriesPinOf(Component owner, Component inside)
    {
        const double inset = ComponentFootprintOverlapChecker.OverlapToleranceMicrometers;
        double minX = inside.PhysicalX + inset;
        double minY = inside.PhysicalY + inset;
        double maxX = inside.PhysicalX + inside.WidthMicrometers - inset;
        double maxY = inside.PhysicalY + inside.HeightMicrometers - inset;
        foreach (var pin in owner.PhysicalPins)
        {
            var (x, y) = pin.GetAbsolutePosition();
            if (x > minX && x < maxX && y > minY && y < maxY)
                return true;
        }
        return false;
    }
}
