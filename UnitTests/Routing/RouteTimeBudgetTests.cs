using System.Diagnostics;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// The router's wall-clock budget per route: a search that runs out of time stops and the wire
/// still gets a route (its fallback when nothing better was found) instead of holding the routing
/// pass; the default budget leaves an ordinary detour around an obstacle untouched.
/// </summary>
public class RouteTimeBudgetTests
{
    private static async Task<(DesignCanvasViewModel Canvas, WaveguideConnectionViewModel Connection)> DetourAsync(TimeSpan budget)
    {
        var canvas = new DesignCanvasViewModel();
        canvas.Router.MaxRouteDuration = budget;
        var start = Place(canvas, 0, 0);
        var end = Place(canvas, 1500, 0);
        for (double y = -500; y <= 500; y += 250)
            Place(canvas, 700, y);
        var connection = canvas.ConnectPins(start.PhysicalPins.First(p => p.Name == "out"),
            end.PhysicalPins.First(p => p.Name == "in"))!;
        await canvas.RecalculateRoutesAsync();
        return (canvas, connection);
    }

    [Fact]
    public async Task DefaultBudget_RoutesTheDetour()
    {
        var (_, connection) = await DetourAsync(WaveguideRouter.DefaultMaxRouteDuration);

        connection.Connection.RoutedPath.ShouldNotBeNull();
        connection.Connection.IsBlockedFallback.ShouldBeFalse();
    }

    [Fact]
    public async Task ExhaustedBudget_StillYieldsARoute_WithoutHoldingThePass()
    {
        var clock = Stopwatch.StartNew();
        var (_, connection) = await DetourAsync(TimeSpan.Zero);

        connection.Connection.RoutedPath.ShouldNotBeNull();
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TimedOutWire_IsNotRememberedAsUnroutable_SoALaterPassRetriesIt()
    {
        var (canvas, connection) = await DetourAsync(TimeSpan.Zero);
        await canvas.RecalculateRoutesAsync();
        await canvas.RecalculateRoutesAsync();

        var path = connection.Connection.RoutedPath.ShouldNotBeNull();
        if (path.IsBlockedFallback)
            path.SearchTimedOut.ShouldBeTrue();
        canvas.ConnectionManager.UnroutableWires.IsRemembered(connection.Connection).ShouldBeFalse(
            "running out of time proves nothing about the wire");

        canvas.Router.MaxRouteDuration = WaveguideRouter.DefaultMaxRouteDuration;
        canvas.ConnectionManager.RequestReroute(connection.Connection);
        await canvas.RecalculateRoutesAsync();
        connection.Connection.IsBlockedFallback.ShouldBeFalse();
    }

    [Fact]
    public void Clone_KeepsTheBudget()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.Router.MaxRouteDuration = TimeSpan.FromMilliseconds(1234);
        canvas.Router.InitializePathfindingGrid(0, 0, 1000, 1000, Array.Empty<Component>());

        canvas.Router.CreateIsolatedRoutingClone().MaxRouteDuration.ShouldBe(TimeSpan.FromMilliseconds(1234));
    }

    private static Component Place(DesignCanvasViewModel canvas, double x, double y)
    {
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        (component.PhysicalX, component.PhysicalY) = (x, y);
        canvas.AddComponent(component);
        return component;
    }
}
