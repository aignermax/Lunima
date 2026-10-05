using System.Collections.ObjectModel;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// Maps every logic gate group of a design to the gate id the assembled network uses
/// (issue #1398). Mirrors the traversal of
/// <c>CAP_Core.Analysis.LogicAnalysis.LogicNetworkAssembler.CollectGates</c> exactly:
/// a group carrying a persisted <see cref="TruthTablePinAssignment"/> is a gate and
/// its subtree is its own behaviour; a plain wrapper group (a hierarchical cell
/// instance) is descended into, extending the path — so a gate nested inside a cell
/// instance keys as <c>&lt;topGroup&gt;/&lt;…&gt;/&lt;gateGroup&gt;</c> while a
/// top-level gate keeps its plain group name. The canvas renderers resolve badge and
/// marker positions through this map, so nested gates (the RAM's register bits)
/// carry their live chips exactly like top-level ones.
/// </summary>
internal static class LogicGateGroupLocator
{
    /// <summary>
    /// Builds the gate-id → group map of one design: one entry per group with a
    /// persisted pin assignment, nested ones keyed by their hierarchical path.
    /// </summary>
    /// <param name="topLevel">The canvas's top-level components.</param>
    /// <returns>Gate id → gate group, in depth-first traversal order.</returns>
    public static Dictionary<string, ComponentGroup> BuildMap(
        ObservableCollection<ComponentViewModel> topLevel)
    {
        var map = new Dictionary<string, ComponentGroup>();
        Collect(topLevel.Select(vm => vm.Component), ancestorPath: null, map);
        return map;
    }

    /// <summary>Depth-first gate collection — one stack frame per wrapper level, no per-frame cost.</summary>
    private static void Collect(
        IEnumerable<Component> components,
        string? ancestorPath,
        Dictionary<string, ComponentGroup> map)
    {
        foreach (var group in components.OfType<ComponentGroup>())
        {
            var path = ancestorPath == null ? group.GroupName : $"{ancestorPath}/{group.GroupName}";
            if (group.TruthTablePinAssignment != null)
            {
                map[path] = group;
                continue;
            }
            Collect(group.ChildComponents, path, map);
        }
    }
}
