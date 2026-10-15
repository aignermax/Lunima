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
    /// <summary>Known crossings per example (defaults to 0; the group re-route bake removed the last two).</summary>
    private static readonly Dictionary<string, int> KnownCrossings = new();

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
        var counter = new FrozenCrossingCounter();

        int crossings = counter.CrossingsBelow(topLevel, groups) + groups.Sum(counter.CrossingsInside);

        crossings.ShouldBe(KnownCrossings.GetValueOrDefault(exampleFileName),
            $"'{exampleFileName}': wires cut through frozen group paths without a crossing component");
    }

    /// <summary>
    /// Counts the crossings with <see cref="SampledPolyline"/> — the sampled form behind
    /// <see cref="PathIntersectionDetector.Crosses"/>, same verdict — sampling every path once:
    /// a nested frozen path is compared with the wires of each level above it, and re-sampling
    /// it per pair made the largest examples take seconds.
    /// </summary>
    private sealed class FrozenCrossingCounter
    {
        private readonly Dictionary<RoutedPath, SampledPolyline> _sampled = new(ReferenceEqualityComparer.Instance);

        /// <summary>Crossings of a group's own wires with the frozen paths of the groups nested in it, recursively.</summary>
        public int CrossingsInside(ComponentGroup group)
        {
            var children = group.ChildComponents.OfType<ComponentGroup>().ToList();
            return CrossingsBelow(Routed(group.InternalPaths), children) + children.Sum(CrossingsInside);
        }

        /// <summary>Crossings of <paramref name="wires"/> with every frozen path inside <paramref name="groups"/> (any depth).</summary>
        public int CrossingsBelow(IReadOnlyList<RoutedPath> wires, IReadOnlyList<ComponentGroup> groups)
        {
            var frozen = Routed(groups
                .SelectMany(g => g.GetAllComponentsRecursive().OfType<ComponentGroup>().Prepend(g))
                .SelectMany(g => g.InternalPaths));
            var sampledWires = wires.Select(Sample).ToList();
            return frozen.Select(Sample).Sum(path => sampledWires.Count(path.Crosses));
        }

        private SampledPolyline Sample(RoutedPath path)
        {
            if (!_sampled.TryGetValue(path, out var sampled))
                _sampled[path] = sampled = SampledPolyline.From(path);
            return sampled;
        }

        private static List<RoutedPath> Routed(IEnumerable<FrozenWaveguidePath> paths) =>
            paths.Where(p => !p.Path.IsBlockedFallback && p.Path.Segments.Count > 0).Select(p => p.Path).ToList();
    }
}
