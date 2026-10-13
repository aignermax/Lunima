using System.Globalization;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using Component = CAP_Core.Components.Core.Component;

namespace CAP_Core.Analysis.Hierarchy;

/// <summary>
/// Finds dangling optical pins at every depth of the group hierarchy. A pin counts as
/// connected when a top-level connection or a frozen group path (any depth) ends on it;
/// group pins are resolved to the leaf pin they expose, so wiring a group's exposed pin
/// and wiring the nested component directly are the same thing. A nested pin that is
/// exposed all the way up to its top-level group is the group's interface and is not
/// reported; a nested pin that is neither wired inside its group nor exposed is.
/// </summary>
public class UnconnectedPinChecker
{
    /// <summary>
    /// Checks every optical pin of every leaf component reachable from
    /// <paramref name="components"/> and reports those that are unconnected, not an
    /// external port, and not part of a top-level group's exposed interface.
    /// </summary>
    /// <param name="components">Placed items (top-level components and groups).</param>
    /// <param name="connections">Top-level waveguide connections.</param>
    /// <param name="externalPortPins">Pins that are external ports and should be skipped. Optional.</param>
    /// <returns>One issue per dangling pin, empty when every optical pin is accounted for.</returns>
    public List<DesignIssue> Check(
        IEnumerable<Component> components,
        IEnumerable<WaveguideConnection> connections,
        IEnumerable<PhysicalPin>? externalPortPins = null)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(connections);

        var hierarchy = DesignHierarchy.From(components);
        var connected = CollectConnectedPins(hierarchy, connections);
        var external = ToLeafPinSet(externalPortPins ?? Array.Empty<PhysicalPin>());

        var issues = new List<DesignIssue>();
        foreach (var leaf in hierarchy.Leaves)
        {
            foreach (var pin in leaf.PhysicalPins)
            {
                if (pin.MatterType != MatterType.Light) continue;
                if (connected.Contains(pin) || external.Contains(pin)) continue;
                if (IsExposedByTopLevelGroup(pin)) continue;
                issues.Add(CreateIssue(pin));
            }
        }
        return issues;
    }

    private static HashSet<PhysicalPin> CollectConnectedPins(
        DesignHierarchy hierarchy, IEnumerable<WaveguideConnection> connections)
    {
        var endpoints = connections
            .SelectMany(c => new[] { c.StartPin, c.EndPin })
            .Concat(hierarchy.FrozenPaths.SelectMany(p => new[] { p.Frozen.StartPin, p.Frozen.EndPin }))
            .OfType<PhysicalPin>();
        return ToLeafPinSet(endpoints);
    }

    private static HashSet<PhysicalPin> ToLeafPinSet(IEnumerable<PhysicalPin> pins) =>
        new(pins.Select(DesignHierarchy.ResolveToLeafPin), ReferenceEqualityComparer.Instance);

    /// <summary>
    /// True when every enclosing group, innermost to outermost, exposes the pin — it is
    /// then part of the top-level group's interface. False for top-level components.
    /// </summary>
    private static bool IsExposedByTopLevelGroup(PhysicalPin pin)
    {
        var group = pin.ParentComponent.ParentGroup as ComponentGroup;
        if (group is null)
            return false;

        for (; group != null; group = group.ParentGroup)
        {
            bool exposed = group.ExternalPins.Any(e =>
                e.InternalPin is not null && ReferenceEquals(DesignHierarchy.ResolveToLeafPin(e.InternalPin), pin));
            if (!exposed)
                return false;
        }
        return true;
    }

    private static DesignIssue CreateIssue(PhysicalPin pin)
    {
        var (x, y) = pin.GetAbsolutePosition();
        var name = DesignHierarchy.QualifyName(pin.ParentComponent, $"{pin.ParentComponent.Identifier}.{pin.Name}");
        return new DesignIssue(
            DesignIssueType.UnconnectedPin,
            connection: null,
            x,
            y,
            string.Create(CultureInfo.InvariantCulture, $"Unconnected pin: {name} at ({x}, {y})"),
            localizationKey: "DesignChecks.UnconnectedPin",
            localizationArgs: new object[] { name, x, y });
    }
}
