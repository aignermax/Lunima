using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using CAP_Core.Routing.Snapshots;
using Shouldly;
using UnitTests.Routing.Snapshots;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// The contention-repair pass's deterministic work budget: one node-expansion counter shared
/// by every route of every attempt replaces the former wall-clock budget, so how far a repair
/// gets depends on the design and the budget only — never on how fast or busy the machine is.
/// The scene is the textbook corridor detour that one accepted attempt repairs.
/// </summary>
public class ContentionRepairWorkBudgetTests
{
    [Fact]
    public void ZeroBudget_RunsNoAttempt()
    {
        var (manager, _, connB) = ContentionRepairTests.BuildCorridorDetourScene();
        var blockedPath = connB.RoutedPath;
        manager.ContentionRepairWorkBudget = 0;

        manager.RepairContentionBlockedWires();

        manager.LastContentionRepairAttemptCount.ShouldBe(0);
        manager.LastContentionRepairAcceptCount.ShouldBe(0);
        manager.LastContentionRepairNodesExpanded.ShouldBe(0);
        connB.RoutedPath.ShouldBeSameAs(blockedPath);
    }

    [Fact]
    public void DefaultBudget_CoversTheTextbookRepair()
    {
        var (manager, _, _) = ContentionRepairTests.BuildCorridorDetourScene();

        manager.RepairContentionBlockedWires();

        manager.ContentionRepairWorkBudget.ShouldBe(ContentionRepairService.DefaultWorkBudget);
        manager.LastContentionRepairAcceptCount.ShouldBe(1);
        manager.LastContentionRepairNodesExpanded.ShouldBeInRange(1, ContentionRepairService.DefaultWorkBudget - 1);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(2)]
    public void BudgetCutRepair_GivesIdenticalResults_OnTwoFreshRuns(int budgetDivisor)
    {
        long budget = NodesTheRepairNeeds() / budgetDivisor;

        var first = RunRepair(budget);
        var second = RunRepair(budget);

        first.Attempts.ShouldBe(1);
        first.Accepts.ShouldBe(0, "the budget runs out before the attempt completes");
        first.NodesExpanded.ShouldBe(budget, "the pass stops exactly where its budget ends");
        first.Before.ShouldBe(first.After, "a budget-cut attempt is restored exactly");
        second.ShouldBe(first);
    }

    [Fact]
    public void BudgetBoundary_SpendingTheLastExpansionCutsTheRepair_OneSpareAcceptsIt()
    {
        long needed = NodesTheRepairNeeds();

        var oneShort = RunRepair(needed - 1);
        var exact = RunRepair(needed);
        var oneSpare = RunRepair(needed + 1);

        oneShort.Accepts.ShouldBe(0);
        oneShort.Before.ShouldBe(oneShort.After);
        exact.Accepts.ShouldBe(0, "the route that spent the last expansion may have stopped because of it");
        exact.Before.ShouldBe(exact.After);
        oneSpare.Accepts.ShouldBe(1);
        oneSpare.NodesExpanded.ShouldBe(needed);
    }

    [Fact]
    public void RoutingSnapshot_KeepsTheRepairBudget()
    {
        var fixture = new RoutingSnapshotFixture();
        fixture.Manager.ContentionRepairWorkBudget = 31_337;

        var graph = RoutingInputSnapshot.Capture(fixture.Components, fixture.Router, fixture.Manager)
            .CreatePrivateGraph();

        graph.Manager.ContentionRepairWorkBudget.ShouldBe(31_337);
    }

    /// <summary>Expansions the textbook repair spends when nothing cuts it short.</summary>
    private static long NodesTheRepairNeeds()
    {
        var unbounded = RunRepair(long.MaxValue);
        unbounded.Accepts.ShouldBe(1, "without a budget limit the textbook repair is accepted");
        return unbounded.NodesExpanded;
    }

    /// <summary>Runs the repair pass on a fresh textbook scene with <paramref name="budget"/>.</summary>
    private static RepairOutcome RunRepair(long budget)
    {
        var (manager, connA, connB) = ContentionRepairTests.BuildCorridorDetourScene();
        manager.ContentionRepairWorkBudget = budget;
        string before = Describe(connA) + Describe(connB);

        manager.RepairContentionBlockedWires();

        return new RepairOutcome(manager.LastContentionRepairAttemptCount, manager.LastContentionRepairAcceptCount,
            manager.LastContentionRepairNodesExpanded, before, Describe(connA) + Describe(connB));
    }

    /// <summary>The blocked flag and every exact segment end point as one comparable string.</summary>
    private static string Describe(WaveguideConnection connection) =>
        $"[{connection.IsBlockedFallback}:" + string.Join(";", connection.RoutedPath!.Segments.Select(segment =>
            FormattableString.Invariant(
                $"{segment.StartPoint.X:R},{segment.StartPoint.Y:R}->{segment.EndPoint.X:R},{segment.EndPoint.Y:R}"))) + "]";

    private sealed record RepairOutcome(int Attempts, int Accepts, long NodesExpanded, string Before, string After);
}
