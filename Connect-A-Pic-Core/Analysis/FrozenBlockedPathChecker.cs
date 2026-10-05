using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;

namespace CAP_Core.Analysis;

/// <summary>
/// Reports frozen waveguide paths inside <see cref="ComponentGroup"/>s whose
/// <see cref="CAP_Core.Routing.RoutedPath.IsBlockedFallback"/> flag is set — wires that
/// were routed inside a group, could only fall back to a straight line through
/// obstacles, and were then frozen. Top-level connections carry the same flag and are
/// reported by <see cref="DesignValidator"/> itself; without this checker DRC-lite
/// stayed blind to the frozen ones. Groups are traversed recursively so nested group
/// hierarchies are covered.
/// </summary>
public class FrozenBlockedPathChecker
{
    /// <summary>
    /// Checks every frozen path of the given groups (and their nested groups,
    /// recursively) and returns one <see cref="DesignIssueType.BlockedPath"/> issue per
    /// path whose routed geometry is a blocked fallback.
    /// </summary>
    /// <param name="groups">Top-level groups whose frozen internal paths are checked.</param>
    /// <returns>A list of blocked-path issues, empty if no frozen path is blocked.</returns>
    public List<DesignIssue> Check(IEnumerable<ComponentGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var issues = new List<DesignIssue>();
        foreach (var group in groups)
        {
            CheckGroup(group, groupIdentifierPath: null, issues);
        }
        return issues;
    }

    /// <summary>
    /// Checks one group's frozen paths and recurses into nested child groups,
    /// accumulating the identifier path ("OUTER/INNER") for readable locations.
    /// </summary>
    private static void CheckGroup(
        ComponentGroup group,
        string? groupIdentifierPath,
        List<DesignIssue> issues)
    {
        var path = groupIdentifierPath is null
            ? group.Identifier
            : $"{groupIdentifierPath}/{group.Identifier}";

        foreach (var frozen in group.InternalPaths)
        {
            if (frozen.Path?.IsBlockedFallback != true)
                continue;
            issues.Add(CreateIssue(frozen, path));
        }

        foreach (var childGroup in group.ChildComponents.OfType<ComponentGroup>())
        {
            CheckGroup(childGroup, path, issues);
        }
    }

    /// <summary>
    /// Builds the issue for one blocked frozen path. The location is the midpoint of
    /// the first segment (frozen paths are stored in absolute coordinates); the
    /// description names the group path and both endpoint pins when available.
    /// </summary>
    private static DesignIssue CreateIssue(FrozenWaveguidePath frozen, string groupPath)
    {
        var (x, y) = GetLocation(frozen);
        var startName = FormatPin(frozen.StartPin);
        var endName = FormatPin(frozen.EndPin);
        return new DesignIssue(
            DesignIssueType.BlockedPath,
            connection: null,
            x,
            y,
            $"Blocked path in group '{groupPath}': {startName} to {endName}",
            localizationKey: "DesignChecks.BlockedPath.InGroup",
            localizationArgs: new object[] { groupPath, startName, endName });
    }

    /// <summary>
    /// Returns the midpoint of the first segment, or the group's pin midpoint when the
    /// path has no segments, or (0, 0) when neither exists.
    /// </summary>
    private static (double X, double Y) GetLocation(FrozenWaveguidePath frozen)
    {
        if (frozen.Path?.Segments is { Count: > 0 } segments)
        {
            var first = segments[0];
            return ((first.StartPoint.X + first.EndPoint.X) / 2.0,
                    (first.StartPoint.Y + first.EndPoint.Y) / 2.0);
        }

        if (frozen.StartPin is not null && frozen.EndPin is not null)
        {
            var (startX, startY) = frozen.StartPin.GetAbsolutePosition();
            var (endX, endY) = frozen.EndPin.GetAbsolutePosition();
            return ((startX + endX) / 2.0, (startY + endY) / 2.0);
        }

        return (0, 0);
    }

    /// <summary>
    /// Formats a pin as "ComponentId.PinName"; pin-less frozen paths (e.g. imported
    /// route outlines) get a generic label.
    /// </summary>
    private static string FormatPin(PhysicalPin? pin)
    {
        return pin is null
            ? "(unpinned route end)"
            : $"{pin.ParentComponent.Identifier}.{pin.Name}";
    }
}
