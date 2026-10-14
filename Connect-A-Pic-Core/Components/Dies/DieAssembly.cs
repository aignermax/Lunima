using System;
using System.Collections.Generic;
using System.Linq;
using CAP_Core.Components.Core;

namespace CAP_Core.Components.Dies;

/// <summary>
/// Queries over the dies of a multi-die assembly. A design is an assembly as soon as one
/// top-level group carries a <see cref="DieDefinition"/>; a design without die groups is a
/// single-die design whose one die is the classic chip rectangle.
/// </summary>
public static class DieAssembly
{
    /// <summary>Default spacing between a new die and the dies already placed.</summary>
    public const double DefaultDieSpacingMicrometers = 500.0;

    /// <summary>The die groups among <paramref name="topLevelComponents"/>, in canvas order.</summary>
    public static IReadOnlyList<ComponentGroup> DiesOf(IEnumerable<Component> topLevelComponents) =>
        topLevelComponents.OfType<ComponentGroup>().Where(g => g.IsDie).ToList();

    /// <summary>True when at least one top-level component is a die group.</summary>
    public static bool IsAssembly(IEnumerable<Component> topLevelComponents) =>
        topLevelComponents.OfType<ComponentGroup>().Any(g => g.IsDie);

    /// <summary>The absolute outline of a die group.</summary>
    /// <exception cref="ArgumentException">When <paramref name="die"/> is not a die.</exception>
    public static DieOutline OutlineOf(ComponentGroup die)
    {
        var definition = die.Die ?? throw new ArgumentException("Group is not a die.", nameof(die));
        return new DieOutline(
            die.PhysicalX + definition.OutlineOffsetXMicrometers,
            die.PhysicalY + definition.OutlineOffsetYMicrometers,
            definition.WidthMicrometers,
            definition.HeightMicrometers);
    }

    /// <summary>The topmost die whose outline contains the point, or null.</summary>
    public static ComponentGroup? DieAt(IEnumerable<Component> topLevelComponents, double x, double y) =>
        DiesOf(topLevelComponents).LastOrDefault(d => OutlineOf(d).Contains(x, y));

    /// <summary>The die the component belongs to (walking the parent chain), or null.</summary>
    public static ComponentGroup? DieOf(Component? component)
    {
        for (var group = component as ComponentGroup ?? component?.ParentGroup as ComponentGroup;
             group != null;
             group = group.ParentGroup)
        {
            if (group.IsDie)
                return group;
        }
        return null;
    }

    /// <summary>
    /// Bounding outline of all dies (the carrier), or null when there are none.
    /// </summary>
    public static DieOutline? CarrierOutline(IEnumerable<ComponentGroup> dies)
    {
        DieOutline? carrier = null;
        foreach (var die in dies)
        {
            var outline = OutlineOf(die);
            carrier = carrier is { } current ? current.Union(outline) : outline;
        }
        return carrier;
    }

    /// <summary>
    /// Top-left corner for a new die of the given size: to the right of the existing dies,
    /// top-aligned with the first one, separated by <paramref name="spacingMicrometers"/>.
    /// With no dies the origin is (0, 0).
    /// </summary>
    public static (double X, double Y) NextDieOrigin(
        IEnumerable<DieOutline> existing, double spacingMicrometers = DefaultDieSpacingMicrometers)
    {
        var outlines = existing.ToList();
        if (outlines.Count == 0)
            return (0, 0);
        double right = outlines.Max(o => o.Right);
        return (right + spacingMicrometers, outlines[0].Y);
    }

    /// <summary>
    /// True when <paramref name="candidate"/> overlaps any of <paramref name="others"/> —
    /// dies are separate pieces of material and can never share area.
    /// </summary>
    public static bool OverlapsAny(DieOutline candidate, IEnumerable<DieOutline> others) =>
        others.Any(candidate.Overlaps);

    /// <summary>Next free default die name ("Die 1", "Die 2", …) not used by <paramref name="existingNames"/>.</summary>
    public static string NextDieName(IEnumerable<string> existingNames, string prefix = "Die")
    {
        var used = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        for (int i = 1; ; i++)
        {
            var name = $"{prefix} {i}";
            if (!used.Contains(name))
                return name;
        }
    }
}
