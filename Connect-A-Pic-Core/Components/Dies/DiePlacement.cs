using System;
using System.Collections.Generic;
using System.Linq;
using CAP_Core.Components.Core;

namespace CAP_Core.Components.Dies;

/// <summary>
/// Finds a free spot for a part dropped onto a die: inside the outline and clear of the
/// die's other parts by <see cref="MinGapMicrometers"/>, as close as possible to where the
/// user dropped it.
/// </summary>
public static class DiePlacement
{
    /// <summary>Minimum clearance between parts, matching the canvas placement rule.</summary>
    public const double MinGapMicrometers = 5.0;

    private const double SearchStepMicrometers = 25.0;
    private const int AnglesPerRing = 12;

    /// <summary>
    /// Top-left position for a <paramref name="width"/> × <paramref name="height"/> part
    /// requested at (<paramref name="x"/>, <paramref name="y"/>), or null when the die has no room.
    /// </summary>
    public static (double X, double Y)? FindFreeSpot(ComponentGroup die, double width, double height, double x, double y)
    {
        var outline = DieAssembly.OutlineOf(die);
        if (width > outline.Width || height > outline.Height)
            return null;

        var start = Clamp(outline, width, height, x, y);
        if (IsFree(die, outline, width, height, start.X, start.Y))
            return start;

        double maxRadius = Math.Max(outline.Width, outline.Height);
        for (double radius = SearchStepMicrometers; radius <= maxRadius; radius += SearchStepMicrometers)
        {
            for (int i = 0; i < AnglesPerRing; i++)
            {
                double angle = 2 * Math.PI * i / AnglesPerRing;
                var candidate = Clamp(outline, width, height,
                    start.X + radius * Math.Cos(angle), start.Y + radius * Math.Sin(angle));
                if (IsFree(die, outline, width, height, candidate.X, candidate.Y))
                    return candidate;
            }
        }
        return null;
    }

    private static (double X, double Y) Clamp(DieOutline outline, double width, double height, double x, double y) =>
        (Math.Clamp(x, outline.X, outline.Right - width), Math.Clamp(y, outline.Y, outline.Bottom - height));

    /// <summary>
    /// True when a <paramref name="width"/> × <paramref name="height"/> footprint at
    /// (<paramref name="x"/>, <paramref name="y"/>) lies inside <paramref name="die"/>'s outline and
    /// keeps <see cref="MinGapMicrometers"/> from every part of the die not in <paramref name="exclude"/>.
    /// </summary>
    public static bool IsFree(ComponentGroup die, double x, double y, double width, double height,
        IReadOnlySet<Component>? exclude = null)
    {
        ArgumentNullException.ThrowIfNull(die);
        return IsFree(die, DieAssembly.OutlineOf(die), width, height, x, y, exclude);
    }

    /// <summary>The footprint of a die part: a group's content box, a part's own rectangle.</summary>
    public static (double X, double Y, double Width, double Height) FootprintOf(Component component) =>
        component is ComponentGroup g
            ? (g.PhysicalX + g.MinChildOffsetX, g.PhysicalY + g.MinChildOffsetY, g.WidthMicrometers, g.HeightMicrometers)
            : (component.PhysicalX, component.PhysicalY, component.WidthMicrometers, component.HeightMicrometers);

    private static bool IsFree(ComponentGroup die, DieOutline outline, double width, double height, double x, double y,
        IReadOnlySet<Component>? exclude = null)
    {
        if (!outline.ContainsRect(x, y, width, height, OutlineToleranceMicrometers))
            return false;
        return !die.ChildComponents
            .Where(c => c.IsRoutingObstacle && exclude?.Contains(c) != true)
            .Any(c => Overlaps(c, x, y, width, height));
    }

    private const double OutlineToleranceMicrometers = 1e-6;

    private static bool Overlaps(Component other, double x, double y, double width, double height)
    {
        var (ox, oy, ow, oh) = FootprintOf(other);
        return x - MinGapMicrometers < ox + ow && x + width + MinGapMicrometers > ox
            && y - MinGapMicrometers < oy + oh && y + height + MinGapMicrometers > oy;
    }
}
