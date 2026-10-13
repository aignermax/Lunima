using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;
using CAP.Avalonia.Services.AgentBridge.Tools.Session;
using Shouldly;
using UnitTests.Integration;
using Xunit;
using static UnitTests.AgentBridge.RepairTestBench;

namespace UnitTests.AgentBridge;

/// <summary>
/// Repair bake for the shipped examples: drives the agent-bridge repair loop headlessly —
/// <c>suggest_fixes</c> on every blocked wire, <c>apply_suggestion</c> for the best trial that
/// lowers the blocked count, repeated until a sweep finds nothing — and persists the result with
/// <c>save_design</c>, exactly as an agent repairing an example would. The file is only replaced
/// when the reloaded design has fewer blocked wires than the shipped one.
/// <para>
/// Gated by <c>CAP_BAKE_REPAIR=1</c> (unset, the theory is a no-op) and <c>Category=Slow</c>;
/// <c>CAP_BAKE_ONLY</c> restricts it to examples whose file name contains one of its
/// ';'-separated parts, <c>CAP_BAKE_REPAIR_BUDGET_S</c> sets the per-wire trial budget.
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class ExampleRepairBakeTests
{
    private const string BakeEnableVariable = "CAP_BAKE_REPAIR";
    private const string BakeOnlyVariable = "CAP_BAKE_ONLY";
    private const string BudgetVariable = "CAP_BAKE_REPAIR_BUDGET_S";
    private const double DefaultBudgetSeconds = 600;

    /// <summary>File names of every example listed in the manifest.</summary>
    public static TheoryData<string> ExampleFiles => ExampleRouteBakeTests.ExampleFiles;

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public async Task Bake_BlockedWiresThroughRepairSuggestions_NeverRegresses(string exampleFileName)
    {
        if (Environment.GetEnvironmentVariable(BakeEnableVariable) != "1") return;
        var only = Environment.GetEnvironmentVariable(BakeOnlyVariable);
        if (!string.IsNullOrEmpty(only)
            && !only.Split(';', StringSplitOptions.RemoveEmptyEntries).Any(p => exampleFileName.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return;

        var bench = await LoadAsync(exampleFileName);
        if (Environment.GetEnvironmentVariable("CAP_NO_BREAKOUT") == "1") bench.Canvas.Router.UsePinBreakouts = false;
        int before = bench.BlockedCount();
        if (before == 0) return;
        double budget = double.TryParse(Environment.GetEnvironmentVariable(BudgetVariable),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b)
            ? b : DefaultBudgetSeconds;

        while (bench.BlockedCount() > 0 && await TryApplyOneFix(bench, budget)) { }

        int after = bench.BlockedCount();
        Console.WriteLine($"[repair-bake] {exampleFileName}: blocked {before} → {after}");
        if (after >= before) return;

        var tempPath = Path.Combine(Path.GetTempPath(), $"repair-bake-{Guid.NewGuid():N}.lun");
        Body(await new SaveDesignTool(bench.Session).ExecuteAsync(Args(JsonSerializer.Serialize(new { path = tempPath }))));
        var (verify, verifyOps, _) = await MziFringeAnalysis.LoadDesignFromPath(tempPath);
        await verifyOps.PostLoadRouting;
        int reloaded = verify.Connections.Count(c => c.Connection.IsBlockedFallback);
        Console.WriteLine($"[repair-bake] {exampleFileName}: reloaded blocked {reloaded}, artifact {tempPath}");
        verify.Connections.Count(c => c.Connection.RoutedPath == null).ShouldBe(0);
        reloaded.ShouldBeLessThan(before, "the repaired example must reload with fewer blocked wires");
        File.Copy(tempPath, Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName), overwrite: true);
    }

    /// <summary>Suggests fixes for each blocked wire and applies the first one whose trial lowers the blocked count.</summary>
    private static async Task<bool> TryApplyOneFix(RepairTestBench bench, double budgetSeconds)
    {
        int current = bench.BlockedCount();
        var blockedIds = bench.Canvas.Connections.Where(c => c.Connection.IsBlockedFallback)
            .Select(c => c.Connection.Id.ToString()).ToList();
        foreach (var id in blockedIds)
        {
            if (bench.Canvas.Connections.All(c => c.Connection.Id.ToString() != id)) continue;
            var result = await new SuggestFixesTool(bench.Session).ExecuteAsync(
                Args(JsonSerializer.Serialize(new { connection_id = id, time_budget_s = budgetSeconds })));
            if (result.IsError) continue;
            var body = Body(result);
            var best = body.GetProperty("suggestions").EnumerateArray()
                .FirstOrDefault(s => s.GetProperty("blocked_after").GetInt32() < current
                    && s.GetProperty("blocked_after").GetInt32() >= 0);
            if (best.ValueKind == JsonValueKind.Undefined) continue;
            var applied = await new ApplySuggestionTool(bench.Session).ExecuteAsync(
                Args(JsonSerializer.Serialize(new { id = best.GetProperty("id").GetString() })));
            Console.WriteLine($"[repair-bake] applied {best.GetProperty("explanation").GetString()}: " +
                              $"{(applied.IsError ? applied.Content[0].Text : "ok")}, blocked now {bench.BlockedCount()}");
            if (!applied.IsError && bench.BlockedCount() < current) return true;
        }
        return false;
    }
}
