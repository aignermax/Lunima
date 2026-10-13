using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Component = CAP_Core.Components.Core.Component;

namespace CAP_Core.Analysis.Hierarchy;

/// <summary>
/// Reports the router's geometry verdicts carried by frozen group paths at any nesting
/// depth — the frozen counterpart of the per-connection checks in
/// <see cref="DesignValidator"/>: an invalid bend geometry and a bend below the process
/// minimum radius. A wire keeps these flags when it is frozen into a group, but has no
/// live connection any more for the connection-level checks to see. Blocked fallbacks are
/// reported by <see cref="FrozenBlockedPathChecker"/>; a frozen route running through a
/// component is detected geometrically by <see cref="FrozenPathFootprintChecker"/>.
/// </summary>
public class FrozenPathFlagChecker
{
    private sealed record FlagRule(
        Func<RoutedPath, bool> IsSet,
        DesignIssueType Type,
        string EnglishPrefix,
        string LocalizationKey);

    private static readonly FlagRule[] Rules =
    {
        new(p => p.IsInvalidGeometry, DesignIssueType.InvalidGeometry,
            "Bend radius violation", "DesignChecks.InvalidGeometry.InGroup"),
        new(p => p.ViolatesProcessMinBendRadius, DesignIssueType.BendRadiusBelowProcessMinimum,
            "Bend radius below process minimum", "DesignChecks.BendRadiusBelowProcessMinimum.InGroup"),
    };

    /// <summary>
    /// Checks every frozen path of the given groups and their nested groups and returns
    /// one issue per raised flag, located on the path in absolute coordinates.
    /// </summary>
    /// <param name="groups">Top-level groups whose frozen paths are checked recursively.</param>
    /// <returns>The flagged frozen paths, empty when every frozen route is clean.</returns>
    public List<DesignIssue> Check(IEnumerable<ComponentGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var issues = new List<DesignIssue>();
        foreach (var path in DesignHierarchy.From(groups.Cast<Component>()).FrozenPaths)
        {
            if (path.Frozen.Path is not { } routed)
                continue;
            foreach (var rule in Rules.Where(r => r.IsSet(routed)))
                issues.Add(CreateIssue(rule, path));
        }
        return issues;
    }

    private static DesignIssue CreateIssue(FlagRule rule, HierarchicalFrozenPath path)
    {
        var (x, y) = FrozenBlockedPathChecker.GetLocation(path.Frozen);
        var startName = FrozenBlockedPathChecker.FormatPin(path.Frozen.StartPin);
        var endName = FrozenBlockedPathChecker.FormatPin(path.Frozen.EndPin);
        return new DesignIssue(
            rule.Type,
            connection: null,
            x,
            y,
            $"{rule.EnglishPrefix} in group '{path.GroupPath}': {startName} to {endName}",
            localizationKey: rule.LocalizationKey,
            localizationArgs: new object[] { path.GroupPath, startName, endName });
    }
}
