using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Census of the shipped examples for bake reports: blocked top-level wires, blocked frozen
/// paths at any group depth, Design Checks problems (unconnected-pin hints excluded) and
/// crossing cells, one line per example. Gated by <c>CAP_EXAMPLE_CENSUS=1</c> (unset, a no-op).
/// </summary>
[Trait("Category", "Slow")]
public class ExampleRoutingCensusTests
{
    private const string EnableVariable = "CAP_EXAMPLE_CENSUS";
    private const string OnlyVariable = "CAP_BAKE_ONLY";

    /// <summary>File names of every example listed in the manifest.</summary>
    public static TheoryData<string> ExampleFiles => ExampleRouteBakeTests.ExampleFiles;

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public async Task Census(string exampleFileName)
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1") return;
        var only = Environment.GetEnvironmentVariable(OnlyVariable);
        if (!string.IsNullOrEmpty(only)
            && !only.Split(';', StringSplitOptions.RemoveEmptyEntries).Any(p => exampleFileName.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return;

        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(exampleFileName);
        await fileOps.PostLoadRouting;
        var placed = canvas.Components.Select(c => c.Component).ToList();
        var groups = placed.OfType<ComponentGroup>().ToList();
        int topBlocked = canvas.Connections.Count(c => c.Connection.IsBlockedFallback);
        int internalBlocked = groups.Sum(GroupInternalRerouteBake.CountBlocked);
        int crossings = placed.Count(CrossingComponentCatalog.IsCrossing)
            + groups.Sum(g => g.GetAllComponentsRecursive().Count(CrossingComponentCatalog.IsCrossing));
        var problems = ExampleGroupRerouteBakeTests.DesignCheckProblems(canvas);
        Console.WriteLine($"[census] {exampleFileName} | top-blocked={topBlocked} | internal-blocked={internalBlocked} | " +
                          $"problems={problems.Values.Sum()} ({ExampleGroupRerouteBakeTests.Describe(problems)}) | crossings={crossings}");
    }
}
