using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Top-level re-bake for the shipped examples: removes every routing-placed crossing, re-routes
/// all top-level wires from scratch (group interiors stay as shipped) and connects the wires
/// that are still blocked through crossing chains. Earlier bakes kept each wire's old route and
/// only added crossings for the blocked ones, so a wire that once took a detour (and sealed a
/// neighbour's pin) kept it for good; a from-scratch pass with today's router (pin breakouts,
/// contention repair) finds the planar arrangement where one exists. The file is written back
/// only when the reloaded design has fewer Design Checks problems and no more blocked wires.
/// <para>
/// Gated by <c>CAP_BAKE_TOPLEVEL=1</c> (unset, the theory is a no-op) and <c>Category=Slow</c>;
/// <c>CAP_BAKE_ONLY</c> restricts it to examples whose file name contains one of its
/// ';'-separated parts.
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class ExampleTopLevelRebakeTests
{
    private const string BakeEnableVariable = "CAP_BAKE_TOPLEVEL";
    private const string BakeOnlyVariable = "CAP_BAKE_ONLY";

    /// <summary>File names of every example listed in the manifest.</summary>
    public static TheoryData<string> ExampleFiles => ExampleRouteBakeTests.ExampleFiles;

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public async Task Bake_TopLevelFromScratch_NeverRegresses(string exampleFileName)
    {
        if (Environment.GetEnvironmentVariable(BakeEnableVariable) != "1") return;
        var only = Environment.GetEnvironmentVariable(BakeOnlyVariable);
        if (!string.IsNullOrEmpty(only)
            && !only.Split(';', StringSplitOptions.RemoveEmptyEntries).Any(p => exampleFileName.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return;

        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(exampleFileName);
        await fileOps.PostLoadRouting;
        var problemsBefore = ExampleGroupRerouteBakeTests.DesignCheckProblems(canvas);
        int blockedBefore = ExampleCrossingBakeTests.TotalBlockedPaths(canvas);
        if (problemsBefore.Values.Sum() == 0) return;

        await RerouteTopLevelFromScratchAsync(canvas);
        int chained = ConnectBlockedThroughCrossings(canvas);
        Console.WriteLine($"[toplevel-bake] {exampleFileName}: crossing chains connected {chained}");

        var bakedPath = Path.Combine(Path.GetTempPath(), $"toplevel-bake-{Guid.NewGuid():N}.lun");
        await MziFringeAnalysis.SaveToFileAsync(fileOps, bakedPath);
        var (final, finalOps, _) = await MziFringeAnalysis.LoadDesignFromPath(bakedPath);
        await finalOps.PostLoadRouting;
        final.Connections.Count(c => c.Connection.RoutedPath == null).ShouldBe(0, "every wire reloads with a route");
        var problemsAfter = ExampleGroupRerouteBakeTests.DesignCheckProblems(final);
        int blockedAfter = ExampleCrossingBakeTests.TotalBlockedPaths(final);
        Console.WriteLine($"[toplevel-bake] {exampleFileName}: blocked {blockedBefore} → {blockedAfter}, design-check problems " +
                          $"{ExampleGroupRerouteBakeTests.Describe(problemsBefore)} → {ExampleGroupRerouteBakeTests.Describe(problemsAfter)}, artifact {bakedPath}");
        if (problemsAfter.Values.Sum() >= problemsBefore.Values.Sum() || blockedAfter > blockedBefore) return;

        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName);
        File.WriteAllText(examplePath, ExampleCrossingBakeTests.KeepShippedProcess(examplePath, bakedPath));
    }

    /// <summary>
    /// Removes the routing-placed crossings (their wires merge pin to pin) and routes every
    /// top-level wire again without its cached geometry.
    /// </summary>
    internal static async Task RerouteTopLevelFromScratchAsync(DesignCanvasViewModel canvas)
    {
        new CleanUpCrossingsCommand(canvas).Execute();
        await WaitForRoutingAsync(canvas);
        foreach (var connection in canvas.ConnectionManager.Connections.ToList())
        {
            connection.IsRouteFrozen = false;
            connection.InvalidateRoute();
            canvas.ConnectionManager.RequestReroute(connection);
        }
        await canvas.RecalculateRoutesAsync();
        await WaitForRoutingAsync(canvas);
    }

    /// <summary>Connects blocked top-level wires through crossing chains with the offline search budgets.</summary>
    internal static int ConnectBlockedThroughCrossings(DesignCanvasViewModel canvas)
    {
        if (!canvas.ConnectionManager.Connections.Any(c => c.IsBlockedFallback)) return 0;
        var templates = TestPdkLoader.LoadAllTemplates();
        var preferred = CrossingComponentInstance.PreferredPdksOf(canvas.Components.Select(c => c.Component), templates);
        var binder = new CrossingInsertionCanvasBinder(
            canvas, () => CrossingComponentInstance.CreateFromTemplates(templates, preferred), action => action());
        binder.Service.ChainPassTimeBudget = TimeSpan.MaxValue;
        binder.Service.FallbackChainSearches = new[] { (40_000_000, 1.0), (8_000_000, 2.5) };
        int connected = binder.Service.ConnectBlockedWiresThroughCrossings(canvas.ConnectionManager, canvas.Router);
        binder.IsEnabled = false;
        return connected;
    }

    private static async Task WaitForRoutingAsync(DesignCanvasViewModel canvas)
    {
        while (canvas.Routing.IsRouting)
            await Task.Delay(50);
    }
}
