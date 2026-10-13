using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using Component = CAP_Core.Components.Core.Component;

namespace CAP_Core.Analysis.Hierarchy;

/// <summary>
/// Runs the <see cref="WaveguideMinWidthChecker"/> rule over frozen group paths at any
/// nesting depth. A frozen path that still references its endpoint pins carries
/// everything the rule needs — its drawn width and the pins' GDS layer — so it is checked
/// exactly like the live connection it was frozen from, including the per-connection
/// rule resolution (issue #936) when a resolver is wired. Pin-less frozen geometry
/// (GDS-imported outlines) has no layer association and stays out of scope, as before.
/// </summary>
public class FrozenPathMinWidthChecker
{
    private readonly WaveguideMinWidthChecker _minWidthChecker = new();

    /// <summary>
    /// Checks every pinned frozen path of the given groups (recursively).
    /// </summary>
    /// <param name="groups">Top-level groups whose frozen paths are checked.</param>
    /// <param name="canvasWideWidthRules">Design-wide rules; used when no resolver is wired or it has no opinion.</param>
    /// <param name="connectionDrcRuleProvider">Optional per-connection rule resolver (issue #936).</param>
    /// <returns>One issue per frozen path narrower than its cross-section minimum.</returns>
    public List<DesignIssue> Check(
        IEnumerable<ComponentGroup> groups,
        IReadOnlyList<WaveguideMinWidthRule>? canvasWideWidthRules,
        Func<WaveguideConnection, ConnectionDrcRules?>? connectionDrcRuleProvider)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var proxies = DesignHierarchy.From(groups.Cast<Component>()).FrozenPaths
            .Select(p => p.Frozen)
            .Where(f => f.StartPin is not null && f.EndPin is not null)
            .Select(ToProxyConnection)
            .ToList();
        if (proxies.Count == 0)
            return new List<DesignIssue>();

        var findings = _minWidthChecker.CheckConnections(
            proxies,
            proxy => connectionDrcRuleProvider?.Invoke(proxy)?.WidthRules ?? canvasWideWidthRules);
        return findings.Select(DetachProxy).ToList();
    }

    /// <summary>
    /// A transient connection view of the frozen path for the width rule; it never
    /// reaches the canvas — see <see cref="DetachProxy"/>.
    /// </summary>
    private static WaveguideConnection ToProxyConnection(FrozenWaveguidePath frozen) =>
        new()
        {
            StartPin = DesignHierarchy.ResolveToLeafPin(frozen.StartPin!),
            EndPin = DesignHierarchy.ResolveToLeafPin(frozen.EndPin!),
            WidthMicrometers = frozen.WidthMicrometers,
        };

    /// <summary>
    /// Drops the proxy connection from the finding: the canvas has no live connection to
    /// highlight for a frozen path, so the issue navigates by position only.
    /// </summary>
    private static DesignIssue DetachProxy(DesignIssue issue) =>
        new(issue.Type, connection: null, issue.X, issue.Y, issue.Description,
            issue.LocalizationKey, issue.LocalizationArgs);
}
