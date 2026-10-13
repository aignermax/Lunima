using CAP.Avalonia.Services;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels.Workspaces;

/// <summary>
/// Picks the workspace a design opens in when its .lun file stores none (every example and
/// every file saved before workspaces existed). Decided from the design's content, first
/// match wins:
/// <list type="number">
/// <item><see cref="Workspace.System"/> — the design is a multi-chiplet assembly: at least
/// one cross-chiplet edge-coupler link, or two or more top-level groups bound to a
/// fabrication process (chiplets).</item>
/// <item><see cref="Workspace.Logic"/> — at least one group (nested ones included) carries a
/// truth-table pin assignment, i.e. is a logic gate.</item>
/// <item><see cref="Workspace.Optics"/> — the design has a light source.</item>
/// <item><see cref="Workspace.Layout"/> — everything else, including a new empty design.</item>
/// </list>
/// </summary>
public static class WorkspaceDefaults
{
    private const int MinimumChipletsForSystem = 2;

    /// <summary>Returns the default workspace for a design with the given content.</summary>
    /// <param name="topLevelComponents">The design's top-level components (groups included).</param>
    /// <param name="connections">The design's canvas connections.</param>
    public static Workspace ChooseFor(
        IReadOnlyCollection<Component> topLevelComponents,
        IReadOnlyCollection<WaveguideConnection> connections)
    {
        ArgumentNullException.ThrowIfNull(topLevelComponents);
        ArgumentNullException.ThrowIfNull(connections);

        if (IsMultiChiplet(topLevelComponents, connections))
            return Workspace.System;
        var all = Flatten(topLevelComponents).ToList();
        if (all.OfType<ComponentGroup>().Any(g => g.TruthTablePinAssignment != null))
            return Workspace.Logic;
        if (all.Any(SimulationService.IsLightSource))
            return Workspace.Optics;
        return Workspace.Layout;
    }

    /// <summary>
    /// True for a multi-chiplet assembly: a cross-chiplet edge-coupler link exists, or at
    /// least two top-level groups are bound to a fabrication process.
    /// </summary>
    public static bool IsMultiChiplet(
        IReadOnlyCollection<Component> topLevelComponents,
        IReadOnlyCollection<WaveguideConnection> connections)
    {
        if (connections.Any(c => c.IsCrossChipletFacetLink))
            return true;
        return topLevelComponents.OfType<ComponentGroup>()
            .Count(g => g.ProcessBinding != null) >= MinimumChipletsForSystem;
    }

    /// <summary>Every component of the design: top-level ones plus all group descendants.</summary>
    internal static IEnumerable<Component> Flatten(IEnumerable<Component> components)
    {
        foreach (var component in components)
        {
            yield return component;
            if (component is not ComponentGroup group)
                continue;
            foreach (var child in Flatten(group.ChildComponents))
                yield return child;
        }
    }
}
