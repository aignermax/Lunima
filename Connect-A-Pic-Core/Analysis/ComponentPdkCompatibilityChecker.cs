using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using Component = CAP_Core.Components.Core.Component;

namespace CAP_Core.Analysis;

/// <summary>
/// Checks whether placed components belong to a PDK that no longer matches the design's
/// active fabrication process (issue #570 follow-up, LC-T4): after a process edit diverges a
/// PDK from the design's locked process, placing NEW components from that PDK is blocked
/// (see <c>SingleProcessPolicy.CheckPlacement</c>), but components already on the canvas are
/// deliberately kept — this surfaces them for manual review instead of silently leaving a
/// manufacturability problem invisible. Uses the same exemption rule as the placement guard
/// (<see cref="SingleProcessPolicy.IsExempt"/>) so built-in and process-agnostic components
/// are never flagged.
/// </summary>
public class ComponentPdkCompatibilityChecker
{
    /// <summary>
    /// Checks every component's PDK against the enabled set and returns one issue per conflict.
    /// </summary>
    /// <param name="components">All placed components to check.</param>
    /// <param name="pdkSourceByComponent">
    /// Each component's resolved PDK source name (or null for built-in/unresolved components,
    /// which are exempt). Resolution is a caller concern — this method only judges names.
    /// </param>
    /// <param name="processAgnosticPdkNames">PDK names exempt from process enforcement (tool libraries).</param>
    /// <param name="enabledPdkNames">
    /// PDK names currently allowed: under an active process lock the lock-derived member set;
    /// without one (Playground/no selection) all loaded PDK names — a component only gets flagged
    /// there when its PDK isn't loaded at all (e.g. trash-deleted while its instances were kept).
    /// </param>
    /// <param name="processLockActive">
    /// Whether a real (non-Playground) fabrication process is active. Only selects the issue
    /// wording: a process-mismatch message would be wrong when no process exists to mismatch.
    /// </param>
    /// <returns>One issue per conflicted component, empty when every component's PDK is exempt or enabled.</returns>
    public List<DesignIssue> Check(
        IEnumerable<Component> components,
        IReadOnlyDictionary<Component, string?> pdkSourceByComponent,
        IReadOnlyCollection<string> processAgnosticPdkNames,
        IReadOnlyCollection<string> enabledPdkNames,
        bool processLockActive = true)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(pdkSourceByComponent);
        ArgumentNullException.ThrowIfNull(processAgnosticPdkNames);
        ArgumentNullException.ThrowIfNull(enabledPdkNames);

        var enabled = new HashSet<string>(enabledPdkNames, StringComparer.OrdinalIgnoreCase);
        var issues = new List<DesignIssue>();

        foreach (var component in components)
        {
            pdkSourceByComponent.TryGetValue(component, out var pdkSource);
            if (SingleProcessPolicy.IsExempt(pdkSource, processAgnosticPdkNames)) continue;
            if (enabled.Contains(pdkSource!)) continue;

            double centerX = component.PhysicalX + component.WidthMicrometers / 2;
            double centerY = component.PhysicalY + component.HeightMicrometers / 2;
            string name = component.HumanReadableName ?? component.Identifier;

            issues.Add(new DesignIssue(
                DesignIssueType.PdkProcessMismatch,
                connection: null,
                x: centerX,
                y: centerY,
                description: processLockActive
                    ? $"'{name}' belongs to '{pdkSource}', which no longer matches the active process."
                    : $"'{name}' belongs to '{pdkSource}', which is not loaded (the PDK may have been deleted or moved)."));
        }

        return issues;
    }
}
