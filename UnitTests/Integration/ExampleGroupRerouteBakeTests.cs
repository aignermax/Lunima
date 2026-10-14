using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Group re-route bake for the shipped examples: every group's frozen internal paths are
/// re-routed from scratch with the current router (<see cref="GroupInternalRerouteBake"/>,
/// pin breakouts included, orphaned crossing cells removed), the design is saved and reloaded
/// (top-level wires the new interiors invalidate re-route on load), and the example is written
/// back only when the Design Checks report fewer problems than for the shipped file. This is
/// how broken wires inside word cells and RAM blocks are resolved after router improvements.
/// <para>
/// Gated by <c>CAP_BAKE_GROUPS=1</c> (unset, the theory is a no-op) and <c>Category=Slow</c>;
/// <c>CAP_BAKE_ONLY</c> restricts it to examples whose file name contains one of its
/// ';'-separated parts.
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class ExampleGroupRerouteBakeTests
{
    private const string BakeEnableVariable = "CAP_BAKE_GROUPS";
    private const string BakeOnlyVariable = "CAP_BAKE_ONLY";
    private const double WavelengthNm = 1550;

    /// <summary>File names of every example listed in the manifest.</summary>
    public static TheoryData<string> ExampleFiles => ExampleRouteBakeTests.ExampleFiles;

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public async Task Bake_GroupInternalPaths_NeverRegresses(string exampleFileName)
    {
        if (Environment.GetEnvironmentVariable(BakeEnableVariable) != "1") return;
        var only = Environment.GetEnvironmentVariable(BakeOnlyVariable);
        if (!string.IsNullOrEmpty(only)
            && !only.Split(';', StringSplitOptions.RemoveEmptyEntries).Any(p => exampleFileName.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return;

        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(exampleFileName);
        await fileOps.PostLoadRouting;
        var groups = ExampleCrossingBakeTests.TopLevelGroups(canvas);
        if (groups.Count == 0) return;
        var problemsBefore = DesignCheckProblems(canvas);

        var templates = TestPdkLoader.LoadAllTemplates();
        var preferred = CrossingComponentInstance.PreferredPdksOf(canvas.Components.Select(c => c.Component), templates);
        Component? NewCrossing() => CrossingComponentInstance.CreateFromTemplates(templates, preferred)?.Component;
        var topLevelPins = canvas.ConnectionManager.Connections
            .SelectMany(c => new[] { c.StartPin, c.EndPin }).ToHashSet();
        var blockedTopLevelPins = canvas.ConnectionManager.Connections.Where(c => c.IsBlockedFallback)
            .SelectMany(c => new[] { c.StartPin, c.EndPin }).ToHashSet();
        int changed = groups.Sum(group => GroupInternalRerouteBake.Bake(group, NewCrossing,
            canvas.Router.MinBendRadiusMicrometers, canvas.ConnectionManager.WaveguideWidthMicrometers,
            (canvas.ChipMaxX, canvas.ChipMaxY), topLevelPins, blockedTopLevelPins, Console.WriteLine));
        if (changed == 0) return;

        var bakedPath = Path.Combine(Path.GetTempPath(), $"group-bake-{Guid.NewGuid():N}.lun");
        await MziFringeAnalysis.SaveToFileAsync(fileOps, bakedPath);
        var (verify, verifyOps, _) = await MziFringeAnalysis.LoadDesignFromPath(bakedPath);
        await verifyOps.PostLoadRouting;
        await verify.RecalculateRoutesAsync();
        int chained = ConnectBlockedThroughCrossings(verify, templates);
        Console.WriteLine($"[group-bake] {exampleFileName}: top-level crossing chains connected {chained}");
        await MziFringeAnalysis.SaveToFileAsync(verifyOps, bakedPath);
        var (final, finalOps, _) = await MziFringeAnalysis.LoadDesignFromPath(bakedPath);
        await finalOps.PostLoadRouting;
        final.Connections.Count(c => c.Connection.RoutedPath == null).ShouldBe(0, "every wire reloads with a route");
        var problemsAfter = DesignCheckProblems(final);
        Console.WriteLine($"[group-bake] {exampleFileName}: design-check problems {Describe(problemsBefore)} → {Describe(problemsAfter)}, artifact {bakedPath}");
        if (problemsAfter.Values.Sum() >= problemsBefore.Values.Sum()) return;

        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName);
        File.WriteAllText(examplePath, ExampleCrossingBakeTests.KeepShippedProcess(examplePath, bakedPath));
    }

    /// <summary>Connects blocked top-level wires through crossing chains (the offline budgets of the crossing bake).</summary>
    private static int ConnectBlockedThroughCrossings(DesignCanvasViewModel canvas,
        IReadOnlyList<CAP.Avalonia.ViewModels.Library.ComponentTemplate> templates)
    {
        if (!canvas.ConnectionManager.Connections.Any(c => c.IsBlockedFallback)) return 0;
        var preferred = CrossingComponentInstance.PreferredPdksOf(canvas.Components.Select(c => c.Component), templates);
        var binder = new CrossingInsertionCanvasBinder(
            canvas, () => CrossingComponentInstance.CreateFromTemplates(templates, preferred), action => action());
        binder.Service.ChainPassTimeBudget = TimeSpan.MaxValue;
        binder.Service.FallbackChainSearches = new[] { (40_000_000, 1.0), (8_000_000, 2.5) };
        return binder.Service.ConnectBlockedWiresThroughCrossings(canvas.ConnectionManager, canvas.Router);
    }

    /// <summary>Design Checks findings per type, as the panel reports them, without the unconnected-pin hints.</summary>
    internal static Dictionary<string, int> DesignCheckProblems(DesignCanvasViewModel canvas)
    {
        var connections = canvas.Connections.Select(vm => vm.Connection).ToList();
        var placed = canvas.Components.Select(vm => vm.Component).ToList();
        var validator = new DesignValidator();
        var issues = validator.Validate(connections, placed.OfType<ComponentGroup>(), placed, null, WavelengthNm);
        issues.AddRange(validator.ValidateComponentBounds(placed, canvas.ChipMaxX, canvas.ChipMaxY));
        return issues.Where(i => i.Type != DesignIssueType.UnconnectedPin)
            .GroupBy(i => i.Type.ToString()).OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    internal static string Describe(Dictionary<string, int> problems) =>
        problems.Count == 0 ? "none" : string.Join(" ", problems.Select(p => $"{p.Key}={p.Value}"));
}
