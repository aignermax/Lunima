using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// The shared node-expansion counter behind the router's per-route work budget, and how A*
/// searches draw from it: each search is capped at what is left, charges what it spent, and the
/// budget's token cancels the rest of the route once nothing is left. A route budget can draw
/// from an enclosing pass budget, which then bounds every route of the pass together.
/// </summary>
public class RouteWorkBudgetTests
{
    [Fact]
    public void Charge_CountsExpansions_AndCancelsTheTokenOnceSpent()
    {
        using var budget = new RouteWorkBudget(100, CancellationToken.None);

        budget.Charge(60);
        budget.IsExhausted.ShouldBeFalse();
        budget.Token.IsCancellationRequested.ShouldBeFalse();

        budget.Charge(40);
        budget.NodesExpanded.ShouldBe(100);
        budget.IsExhausted.ShouldBeTrue();
        budget.Token.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public void Cap_CutsASearchDownToWhatIsLeft()
    {
        using var budget = new RouteWorkBudget(100, CancellationToken.None);
        budget.Cap(500).ShouldBe(100);

        budget.Charge(70);
        budget.Cap(500).ShouldBe(30);
        budget.Cap(10).ShouldBe(10);

        budget.Charge(30);
        budget.Cap(500).ShouldBe(0);
    }

    [Fact]
    public void ZeroBudget_StartsExhausted()
    {
        using var budget = new RouteWorkBudget(0, CancellationToken.None);

        budget.IsExhausted.ShouldBeTrue();
        budget.Token.IsCancellationRequested.ShouldBeTrue();
        budget.Cap(1000).ShouldBe(0);
    }

    [Fact]
    public void CallerCancellation_CancelsTheToken_WithoutSpendingTheBudget()
    {
        using var caller = new CancellationTokenSource();
        using var budget = new RouteWorkBudget(100, caller.Token);

        caller.Cancel();

        budget.Token.IsCancellationRequested.ShouldBeTrue();
        budget.IsExhausted.ShouldBeFalse();
    }

    [Fact]
    public void EnclosingBudget_IsChargedByEveryRoute_AndCapsTheLastOne()
    {
        using var pass = new RouteWorkBudget(300, CancellationToken.None);
        using var firstRoute = new RouteWorkBudget(250, CancellationToken.None, pass);
        firstRoute.Cap(500).ShouldBe(250);
        firstRoute.Charge(200);

        using var secondRoute = new RouteWorkBudget(250, CancellationToken.None, pass);
        secondRoute.Cap(500).ShouldBe(100, "the pass has less left than the route's own budget");
        secondRoute.Charge(100);

        pass.NodesExpanded.ShouldBe(300);
        pass.IsExhausted.ShouldBeTrue();
        secondRoute.NodesExpanded.ShouldBe(100);
        secondRoute.IsExhausted.ShouldBeTrue("its pass is spent");
        secondRoute.Token.IsCancellationRequested.ShouldBeTrue();
        pass.Token.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public void SpentEnclosingBudget_StartsANewRouteExhausted()
    {
        using var pass = new RouteWorkBudget(10, CancellationToken.None);
        pass.Charge(10);

        using var route = new RouteWorkBudget(1000, CancellationToken.None, pass);

        route.IsExhausted.ShouldBeTrue();
        route.Token.IsCancellationRequested.ShouldBeTrue();
        route.Cap(500).ShouldBe(0);
    }

    [Fact]
    public void SpentRouteBudget_LeavesTheEnclosingBudgetOpen()
    {
        using var pass = new RouteWorkBudget(1000, CancellationToken.None);
        using var route = new RouteWorkBudget(100, CancellationToken.None, pass);

        route.Charge(100);

        route.IsExhausted.ShouldBeTrue();
        pass.IsExhausted.ShouldBeFalse();
        pass.Token.IsCancellationRequested.ShouldBeFalse("one wire's budget never ends the whole pass");
    }

    [Fact]
    public void SearchesSharingABudget_GetOnlyWhatTheEarlierOnesLeft()
    {
        var grid = new PathfindingGrid(0, 0, 500, 500, cellSize: 1.0);
        var cost = new RoutingCostCalculator { CellSizeMicrometers = 1.0, MinStraightRunCells = 2 };
        using var budget = new RouteWorkBudget(250, CancellationToken.None);

        var first = new AStarPathfinder(grid, cost) { MaxNodesExpanded = 200, WorkBudget = budget };
        first.FindPath(10, 250, GridDirection.East, 490, 50, GridDirection.East).ShouldBeNull();
        var second = new AStarPathfinder(grid, cost) { MaxNodesExpanded = 200, WorkBudget = budget };
        second.FindPath(10, 250, GridDirection.East, 490, 50, GridDirection.East).ShouldBeNull();

        first.LastSearchNodesExpanded.ShouldBe(200);
        second.LastSearchNodesExpanded.ShouldBe(50);
        second.LastSearchProvedNoPath.ShouldBeFalse("a search cut short by the budget proves nothing");
        budget.NodesExpanded.ShouldBe(250);
        budget.IsExhausted.ShouldBeTrue();
    }

    [Fact]
    public void SearchWithinBudget_FindsTheSamePathAsWithoutOne()
    {
        var grid = new PathfindingGrid(0, 0, 100, 100, cellSize: 1.0);
        var cost = new RoutingCostCalculator { CellSizeMicrometers = 1.0, MinStraightRunCells = 2 };
        using var budget = new RouteWorkBudget(1_000_000, CancellationToken.None);

        var free = new AStarPathfinder(grid, cost).FindPath(5, 50, GridDirection.East, 90, 20, GridDirection.East);
        var budgeted = new AStarPathfinder(grid, cost) { WorkBudget = budget };
        var path = budgeted.FindPath(5, 50, GridDirection.East, 90, 20, GridDirection.East);

        path.ShouldNotBeNull();
        path.Select(n => (n.X, n.Y)).ShouldBe(free.ShouldNotBeNull().Select(n => (n.X, n.Y)));
        budget.NodesExpanded.ShouldBe(budgeted.LastSearchNodesExpanded);
    }
}
