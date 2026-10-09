using CAP_Core.Components.Core;

namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>
/// Docking of cut routes onto a placed crossing: which ports a straight route enters and
/// leaves through, and whether a cut actually ends on its port.
/// </summary>
internal static class CrossingDocking
{
    /// <summary>Largest distance (µm) between a cut and the crossing port it must dock onto.</summary>
    private const double PortMatchToleranceMicrometers = 0.5;

    /// <summary>Largest unit-vector component across the axis for a direction to count as axis-aligned.</summary>
    private const double AxisAlignmentTolerance = 1e-6;

    /// <summary>
    /// The crossing ports a path travelling in <paramref name="direction"/> enters and leaves
    /// through; false for a direction that is not axis-aligned or a crossing lacking them.
    /// </summary>
    /// <param name="crossing">The crossing component.</param>
    /// <param name="direction">Unit travel direction through the crossing.</param>
    /// <param name="entry">The port the path arrives at.</param>
    /// <param name="exit">The port the path continues from.</param>
    public static bool TryResolvePorts(Component crossing, (double X, double Y) direction,
                                        out PhysicalPin entry, out PhysicalPin exit)
    {
        entry = exit = null!;
        bool horizontal = Math.Abs(direction.Y) <= AxisAlignmentTolerance;
        bool vertical = Math.Abs(direction.X) <= AxisAlignmentTolerance;
        if (horizontal == vertical) return false;
        var (entryPin, exitPin) = CrossingPlacement.ResolveThroughPorts(crossing, horizontal, direction);
        if (entryPin == null || exitPin == null) return false;
        (entry, exit) = (entryPin, exitPin);
        return true;
    }

    /// <summary>True when <paramref name="path"/> ends on <paramref name="pin"/> (within the docking tolerance).</summary>
    /// <param name="path">The cut path.</param>
    /// <param name="pin">The crossing port it must reach.</param>
    public static bool EndsAt(RoutedPath path, PhysicalPin pin)
    {
        if (path.Segments.Count == 0) return false;
        var (px, py) = pin.GetAbsolutePosition();
        var (ex, ey) = path.Segments[^1].EndPoint;
        return Math.Abs(px - ex) <= PortMatchToleranceMicrometers && Math.Abs(py - ey) <= PortMatchToleranceMicrometers;
    }
}
