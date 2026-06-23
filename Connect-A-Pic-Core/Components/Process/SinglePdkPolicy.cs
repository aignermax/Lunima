using System;
using System.Collections.Generic;
using System.Linq;

namespace CAP_Core.Components.Process;

/// <summary>
/// Enforces the single-PDK-per-design rule (issue #570).
/// A monolithic chip is fabricated in exactly one process, so all components
/// placed on the canvas must originate from the same PDK.
/// Built-in components (null/empty <c>pdkSource</c>) are exempt from the check —
/// they are process-agnostic adapters (lasers, detectors, …).
/// </summary>
public static class SinglePdkPolicy
{
    /// <summary>
    /// Examines the PDK sources of all components currently in a design and returns
    /// the "active" chip PDK — the one with the most placements. Returns <c>null</c>
    /// when no user PDK component has been placed yet (only built-ins, or empty design).
    /// Used during migration: existing designs without an <c>activePdkName</c> field
    /// acquire one automatically on their next load/save.
    /// </summary>
    /// <param name="pdkSources">
    ///   Sequence of <c>PdkSource</c> values from all loaded components (may contain
    ///   <c>null</c> or empty strings for built-in components).
    /// </param>
    public static string? DetermineActivePdk(IEnumerable<string?> pdkSources)
    {
        var counts = pdkSources
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .GroupBy(s => s!, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.Key, Count: g.Count()))
            .OrderByDescending(t => t.Count)
            .ToList();

        return counts.Count == 0 ? null : counts[0].Name;
    }

    /// <summary>
    /// Returns the set of PDK names that appear in the design but differ from
    /// <paramref name="activePdkName"/>. An empty set means the design is uniform.
    /// Used after migration to report which PDK sources were demoted.
    /// </summary>
    public static IReadOnlyList<string> FindConflictingPdks(
        IEnumerable<string?> pdkSources,
        string activePdkName)
    {
        return pdkSources
            .Where(s => !string.IsNullOrWhiteSpace(s)
                        && !s!.Equals(activePdkName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Checks whether a new component may be placed on a design whose active PDK is
    /// <paramref name="activePdkName"/>.
    /// </summary>
    /// <param name="activePdkName">
    ///   The design's locked PDK, or <c>null</c> if no PDK has been locked yet.
    ///   When <c>null</c> the first user-PDK component that is placed will lock the design.
    /// </param>
    /// <param name="newComponentPdkSource">
    ///   The PDK source of the component to be placed, or <c>null</c> / empty for built-in
    ///   process-agnostic components.
    /// </param>
    /// <returns>
    ///   <c>(IsAllowed: true, BlockReason: null)</c> when placement is permitted;
    ///   <c>(IsAllowed: false, BlockReason: "…")</c> with a user-readable message otherwise.
    /// </returns>
    public static (bool IsAllowed, string? BlockReason) CheckPlacement(
        string? activePdkName,
        string? newComponentPdkSource)
    {
        // Built-in / process-agnostic components are always allowed.
        if (string.IsNullOrWhiteSpace(newComponentPdkSource))
            return (true, null);

        // No PDK locked yet — first user-PDK component locks the design.
        if (string.IsNullOrWhiteSpace(activePdkName))
            return (true, null);

        if (activePdkName.Equals(newComponentPdkSource, StringComparison.OrdinalIgnoreCase))
            return (true, null);

        return (false,
            $"This component belongs to '{newComponentPdkSource}', but the chip is locked to '{activePdkName}'. " +
            "A monolithic design can only use components from one PDK. " +
            "Start a new design to mix PDKs.");
    }
}
