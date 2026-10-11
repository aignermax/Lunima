using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// A wire must never cut through a path frozen inside a group it runs over (a word cell's
/// internal wiring, a gate's own waveguides) without a crossing component — the export would
/// overlap there. Checked on every level: top-level wires against all group interiors, and
/// each group's own wires against the groups nested in it (<see cref="ExampleWaveguideCrossingTests"/>
/// only compares top-level wires with each other). Counts are pinned per example and may only
/// shrink.
/// </summary>
public class ExampleGroupWireCrossingTests
{
    /// <summary>Known crossings per example (defaults to 0) — both predate the crossing bakes.</summary>
    private static readonly Dictionary<string, int> KnownCrossings = new()
    {
        ["Logic Gate Counter 2-bit.lun"] = 1,
        ["Logic Gate RAM 2x2.lun"] = 1,
    };

    /// <summary>File names of every example listed in the manifest.</summary>
    public static TheoryData<string> ExampleFiles => ExampleRouteBakeTests.ExampleFiles;

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public async Task Example_WiresNeverCrossFrozenGroupPaths(string exampleFileName)
    {
        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(exampleFileName);
        await fileOps.PostLoadRouting;
        var topLevel = canvas.ConnectionManager.Connections
            .Where(c => !c.IsBlockedFallback && c.RoutedPath != null).Select(c => c.RoutedPath!).ToList();
        var groups = canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().ToList();

        int crossings = CrossingsBelow(topLevel, groups) + groups.Sum(CrossingsInside);

        crossings.ShouldBe(KnownCrossings.GetValueOrDefault(exampleFileName),
            $"'{exampleFileName}': wires cut through frozen group paths without a crossing component");
    }

    /// <summary>Crossings of a group's own wires with the frozen paths of the groups nested in it, recursively.</summary>
    private static int CrossingsInside(ComponentGroup group)
    {
        var children = group.ChildComponents.OfType<ComponentGroup>().ToList();
        return CrossingsBelow(Routed(group.InternalPaths), children) + children.Sum(CrossingsInside);
    }

    /// <summary>Crossings of <paramref name="wires"/> with every frozen path inside <paramref name="groups"/> (any depth).</summary>
    private static int CrossingsBelow(IReadOnlyList<RoutedPath> wires, IReadOnlyList<ComponentGroup> groups)
    {
        var frozen = Routed(groups
            .SelectMany(g => g.GetAllComponentsRecursive().OfType<ComponentGroup>().Prepend(g))
            .SelectMany(g => g.InternalPaths));
        return frozen.Sum(path => wires.Count(wire => PathIntersectionDetector.Crosses(path, wire)));
    }

    private static List<RoutedPath> Routed(IEnumerable<FrozenWaveguidePath> paths) =>
        paths.Where(p => !p.Path.IsBlockedFallback && p.Path.Segments.Count > 0).Select(p => p.Path).ToList();
}
