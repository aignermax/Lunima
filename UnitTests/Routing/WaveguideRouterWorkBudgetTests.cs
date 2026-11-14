using System.Collections.Immutable;
using CAP_Core.Components;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Routing.Snapshots;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// The router's deterministic work budget per <see cref="WaveguideRouter.Route"/> call: every
/// search of the call draws from one node-expansion counter, so a wire stops at exactly the same
/// point on every run and still gets a route (its flagged fallback when nothing better was found)
/// instead of holding the routing pass. The scene is a wall between two facing pins: the grid
/// search must detour around it, while the straight Manhattan fallback runs through it.
/// </summary>
public class WaveguideRouterWorkBudgetTests
{
    private const double WallX = 475;
    private const double PinY = 500;

    [Fact]
    public void DefaultBudget_RoutesTheDetourAroundTheWall()
    {
        var (router, start, end) = WallScene(WaveguideRouter.DefaultMaxRouteNodesExpanded);

        var path = router.Route(start, end);

        path.IsBlockedFallback.ShouldBeFalse();
        path.DebugGridPath.ShouldNotBeNull("the detour comes from the grid search");
        router.LastRouteNodesExpanded.ShouldBeInRange(1, WaveguideRouter.DefaultMaxRouteNodesExpanded);
    }

    [Fact]
    public void ZeroBudget_YieldsTheBlockedFallback_FlaggedAsBudgetExhausted()
    {
        var (router, start, end) = WallScene(0);

        var path = router.Route(start, end);

        path.IsBlockedFallback.ShouldBeTrue("no search may expand a node, and the fallback runs through the wall");
        path.SearchBudgetExhausted.ShouldBeTrue();
        router.LastRouteNodesExpanded.ShouldBe(0);
    }

    [Fact]
    public void BudgetBoundary_IsExact_ExactlyEnoughRoutes_OneLessFallsBack()
    {
        var (unbounded, start, end) = WallScene(long.MaxValue);
        var reference = unbounded.Route(start, end);
        long needed = unbounded.LastRouteNodesExpanded;

        var (exact, exactStart, exactEnd) = WallScene(needed);
        var routed = exact.Route(exactStart, exactEnd);
        var (short1, shortStart, shortEnd) = WallScene(needed - 1);
        var cut = short1.Route(shortStart, shortEnd);

        routed.IsBlockedFallback.ShouldBeFalse();
        routed.TotalLengthMicrometers.ShouldBe(reference.TotalLengthMicrometers, 1e-9);
        exact.LastRouteNodesExpanded.ShouldBe(needed);
        cut.IsBlockedFallback.ShouldBeTrue("the search that found the route is one expansion short");
        cut.SearchBudgetExhausted.ShouldBeTrue();
        short1.LastRouteNodesExpanded.ShouldBe(needed - 1);
    }

    [Fact]
    public void SameBudget_SameRoute_OnEveryRun()
    {
        var (first, firstStart, firstEnd) = WallScene(WaveguideRouter.DefaultMaxRouteNodesExpanded);
        var (second, secondStart, secondEnd) = WallScene(WaveguideRouter.DefaultMaxRouteNodesExpanded);

        var a = first.Route(firstStart, firstEnd);
        var b = second.Route(secondStart, secondEnd);

        first.LastRouteNodesExpanded.ShouldBe(second.LastRouteNodesExpanded);
        b.Segments.Count.ShouldBe(a.Segments.Count);
        for (int i = 0; i < a.Segments.Count; i++)
        {
            b.Segments[i].StartPoint.ShouldBe(a.Segments[i].StartPoint);
            b.Segments[i].EndPoint.ShouldBe(a.Segments[i].EndPoint);
        }
    }

    [Fact]
    public void CallerCancellation_IsNotReportedAsBudgetExhaustion()
    {
        var (router, start, end) = WallScene(WaveguideRouter.DefaultMaxRouteNodesExpanded);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var path = router.Route(start, end, cancelled.Token);

        path.SearchBudgetExhausted.ShouldBeFalse("the caller stopped the route, the budget did not run out");
    }

    [Fact]
    public void BudgetExhaustedWire_IsNotRememberedAsUnroutable_SoALaterPassRetriesIt()
    {
        var (router, start, end) = WallScene(0);
        var manager = new WaveguideConnectionManager(router);
        var connection = manager.AddConnectionDeferred(start, end);
        manager.RecalculateAllTransmissions();
        manager.RecalculateAllTransmissions();

        connection.RoutedPath.ShouldNotBeNull().SearchBudgetExhausted.ShouldBeTrue();
        manager.UnroutableWires.IsRemembered(connection).ShouldBeFalse(
            "running out of budget proves nothing about the wire");

        router.MaxRouteNodesExpanded = WaveguideRouter.DefaultMaxRouteNodesExpanded;
        manager.RequestReroute(connection);
        manager.RecalculateAllTransmissions();
        connection.IsBlockedFallback.ShouldBeFalse();
    }

    [Fact]
    public void PassBudgetScope_IsChargedByEveryRouteInside_AndReleasedOnDispose()
    {
        var (unbounded, unboundedStart, unboundedEnd) = WallScene(long.MaxValue);
        unbounded.Route(unboundedStart, unboundedEnd);
        long needed = unbounded.LastRouteNodesExpanded;
        var (router, start, end) = WallScene(WaveguideRouter.DefaultMaxRouteNodesExpanded);
        using var pass = new RouteWorkBudget(needed + needed / 2, CancellationToken.None);

        RoutedPath first, second;
        using (router.ChargeRoutesTo(pass))
        {
            first = router.Route(start, end);
            second = router.Route(start, end);
        }
        var afterScope = router.Route(start, end);

        first.IsBlockedFallback.ShouldBeFalse();
        second.IsBlockedFallback.ShouldBeTrue("the pass had only half a route left");
        pass.NodesExpanded.ShouldBe(pass.MaxNodesExpanded);
        afterScope.IsBlockedFallback.ShouldBeFalse("outside the scope only the route's own budget applies");
        pass.NodesExpanded.ShouldBe(pass.MaxNodesExpanded);
    }

    [Fact]
    public void IsolatedClone_KeepsTheBudget()
    {
        var (router, _, _) = WallScene(1234);

        router.CreateIsolatedRoutingClone().MaxRouteNodesExpanded.ShouldBe(1234);
    }

    [Fact]
    public void RoutingSnapshot_KeepsTheBudget()
    {
        var router = new WaveguideRouter { MaxRouteNodesExpanded = 4321 };
        var floors = new RouteProcessFloorTable(ImmutableArray<RouteConnectionValue>.Empty, null);

        var rebuilt = RouteRouterValue.Capture(router).Build(
            Array.Empty<Component>(), new Dictionary<Guid, Component>(), floors);

        rebuilt.MaxRouteNodesExpanded.ShouldBe(4321);
    }

    /// <summary>
    /// Two facing pins with a tall wall between them on an open grid. Direct styled routes are
    /// off, so the route needs the grid search.
    /// </summary>
    private static (WaveguideRouter Router, PhysicalPin Start, PhysicalPin End) WallScene(long budget)
    {
        var router = new WaveguideRouter
        {
            PreferDirectStyledRoutes = false,
            MaxRouteNodesExpanded = budget,
        };
        var wall = Block(WallX, PinY - 200, 50, 400);
        router.InitializePathfindingGrid(0, 0, 1000, 1000, new[] { wall });
        return (router, Pin(100, PinY, 0), Pin(900, PinY, 180));
    }

    private static Component Block(double x, double y, double width, double height)
    {
        var component = new Component(new Dictionary<int, SMatrix>(), new List<Slider>(), "test", "",
            new Part[1, 1] { { new Part(new List<Pin>()) } }, 0, $"Wall_{x}_{y}", DiscreteRotation.R0)
        {
            WidthMicrometers = width,
            HeightMicrometers = height,
            PhysicalX = x,
            PhysicalY = y,
        };
        return component;
    }

    private static PhysicalPin Pin(double x, double y, double angleDegrees) => new()
    {
        Name = "pin",
        AngleDegrees = angleDegrees,
        ParentComponent = Block(x, y, 1, 1),
    };
}
