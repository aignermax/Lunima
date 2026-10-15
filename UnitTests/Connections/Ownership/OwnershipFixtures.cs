using CAP_Core.Components.Core;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.Connections.Ownership;

/// <summary>
/// The ownership scenario of the product owner: group M holds the child groups K1 and K2,
/// each around one part; M exposes the boundary pins of K1 and K2.
/// </summary>
internal sealed class OwnershipFixtures
{
    /// <summary>Part inside K1.</summary>
    public Component P1 { get; }

    /// <summary>Part inside K2.</summary>
    public Component P2 { get; }

    /// <summary>Child group around <see cref="P1"/>.</summary>
    public ComponentGroup K1 { get; }

    /// <summary>Child group around <see cref="P2"/>.</summary>
    public ComponentGroup K2 { get; }

    /// <summary>Parent group holding <see cref="K1"/> and <see cref="K2"/>.</summary>
    public ComponentGroup M { get; }

    /// <summary>Builds M ⊃ {K1 ⊃ P1, K2 ⊃ P2} with P1 at (x, y) and P2 300 µm to its right.</summary>
    public OwnershipFixtures(double x = 100, double y = 100)
    {
        P1 = Part("p1", x, y);
        P2 = Part("p2", x + 300, y);
        K1 = GroupExposingAll("K1", P1);
        K2 = GroupExposingAll("K2", P2);
        M = GroupExposingAll("M", K1, K2);
    }

    /// <summary>M's boundary pin standing for P1's east pin (via K1).</summary>
    public PhysicalPin MPinOfP1East => BoundaryPin(M, P1.PhysicalPins[1]);

    /// <summary>M's boundary pin standing for P2's west pin (via K2).</summary>
    public PhysicalPin MPinOfP2West => BoundaryPin(M, P2.PhysicalPins[0]);

    /// <summary>A group around <paramref name="children"/> exposing every child pin, with synthesized pins.</summary>
    public static ComponentGroup GroupExposingAll(string name, params Component[] children)
    {
        var group = new ComponentGroup(name)
        {
            PhysicalX = children.Min(c => c.PhysicalX),
            PhysicalY = children.Min(c => c.PhysicalY),
        };
        group.AddChildren(children);
        foreach (var pin in children.SelectMany(c => c.PhysicalPins).ToList())
        {
            var (px, py) = pin.GetAbsolutePosition();
            group.AddExternalPin(new GroupPin
            {
                Name = $"{pin.ParentComponent.Identifier}_{pin.Name}",
                InternalPin = pin,
                RelativeX = px - group.PhysicalX,
                RelativeY = py - group.PhysicalY,
                AngleDegrees = pin.GetAbsoluteAngle(),
            });
        }
        group.SyncPhysicalPinsFromExternalPins();
        return group;
    }

    /// <summary>The synthesized pin of <paramref name="group"/> whose boundary chain ends at <paramref name="leaf"/>.</summary>
    public static PhysicalPin BoundaryPin(ComponentGroup group, PhysicalPin leaf) =>
        group.PhysicalPins.Single(p =>
            ReferenceEquals(CAP_Core.Analysis.Hierarchy.DesignHierarchy.ResolveToLeafPin(p), leaf));
}
