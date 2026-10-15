using System;
using System.Collections.Generic;
using System.Linq;
using CAP_Core.Components.Core;

namespace CAP_Core.Components.Dies;

/// <summary>
/// Keeps a die's external pins in step with its content. Parts are added to and removed
/// from a die while it lives on the assembly, so — unlike a frozen group — its interface
/// changes: every child pin not consumed by an internal waveguide is exposed (that is how
/// an edge coupler's facet becomes linkable from the assembly), and pins of removed parts
/// or pins now wired inside the die disappear unless a wire on the assembly still uses them.
/// </summary>
public static class DiePinExposure
{
    /// <summary>Exposes free child pins and drops stale external pins of <paramref name="die"/>.</summary>
    /// <param name="die">The die group.</param>
    /// <param name="pinsWiredOutside">Child pins that wires on the assembly connect to; never dropped.</param>
    /// <returns>The external pins added by this call.</returns>
    public static IReadOnlyList<GroupPin> Refresh(ComponentGroup die, IReadOnlySet<PhysicalPin> pinsWiredOutside)
    {
        ArgumentNullException.ThrowIfNull(die);
        ArgumentNullException.ThrowIfNull(pinsWiredOutside);

        var childPins = ChildInterfacePins(die).ToList();
        var childPinSet = new HashSet<PhysicalPin>(childPins);
        var wiredInside = WiredInside(die);

        foreach (var stale in die.ExternalPins
                     .Where(p => !pinsWiredOutside.Contains(p.InternalPin)
                                 && (!childPinSet.Contains(p.InternalPin) || wiredInside.Contains(p.InternalPin)))
                     .ToList())
            die.RemoveExternalPin(stale);

        var exposed = new HashSet<PhysicalPin>(die.ExternalPins.Select(p => p.InternalPin));
        var usedNames = new HashSet<string>(die.ExternalPins.Select(p => p.Name), StringComparer.Ordinal);
        var added = new List<GroupPin>();
        foreach (var pin in childPins.Where(p => !exposed.Contains(p) && !wiredInside.Contains(p)))
        {
            var groupPin = Expose(die, pin, UniqueName(pin, usedNames));
            die.AddExternalPin(groupPin);
            added.Add(groupPin);
        }
        return added;
    }

    /// <summary>The pins a die's direct children offer: leaf pins and nested groups' exposed pins.</summary>
    private static IEnumerable<PhysicalPin> ChildInterfacePins(ComponentGroup die) =>
        die.ChildComponents.SelectMany(child => child is ComponentGroup nested
            ? nested.ExternalPins.Select(p => p.InternalPin)
            : child.PhysicalPins);

    private static HashSet<PhysicalPin> WiredInside(ComponentGroup die)
    {
        var wired = new HashSet<PhysicalPin>();
        foreach (var path in die.InternalPaths)
        {
            if (path.StartPin != null) wired.Add(path.StartPin);
            if (path.EndPin != null) wired.Add(path.EndPin);
        }
        return wired;
    }

    private static GroupPin Expose(ComponentGroup die, PhysicalPin pin, string name)
    {
        var (x, y) = pin.GetAbsolutePosition();
        return new GroupPin
        {
            Name = name,
            InternalPin = pin,
            RelativeX = x - die.PhysicalX,
            RelativeY = y - die.PhysicalY,
            AngleDegrees = pin.GetAbsoluteAngle(),
        };
    }

    private static string UniqueName(PhysicalPin pin, HashSet<string> usedNames)
    {
        var baseName = $"{pin.ParentComponent.Identifier}_{pin.Name}";
        var name = baseName;
        for (int i = 2; !usedNames.Add(name); i++)
            name = $"{baseName}_{i}";
        return name;
    }
}
