using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Routing.CrossingInsertion;

/// <summary>
/// A wire the router cannot place with its current pins (in an earlier bake of the counter,
/// a splitter output into an 8 µm MMI input pair) is remembered after it failed again with unchanged pins: later
/// passes keep its blocked placeholder instead of searching it again or starting the
/// ordering cascade for it, until a pin moves or the user re-routes it.
/// </summary>
public class UnroutableWireTests
{
    [Fact]
    public async Task Counter_UnroutableWire_IsRememberedAndNotRetried()
    {
        var scenario = await CounterCrossingScenario.LoadAsync(adaptive: false, sealedPinFixture: true);
        var unroutable = scenario.Manager.Connections.Single(c => c.IsBlockedFallback);

        await scenario.Canvas.RecalculateRoutesAsync();
        scenario.Manager.UnroutableWires.IsRemembered(unroutable).ShouldBeTrue(
            "the wire failed again with unchanged pins");

        var placeholder = unroutable.RoutedPath;
        await scenario.Canvas.RecalculateRoutesAsync();

        unroutable.RoutedPath.ShouldBeSameAs(placeholder, "a remembered wire is not searched again");
        unroutable.IsBlockedFallback.ShouldBeTrue("it stays visibly blocked");
        unroutable.RoutedPath!.FailureReason.ShouldBe(CAP_Core.Routing.RoutingFailureReason.Contention);
        scenario.Manager.LastRoutingPassTimings!.OrderingAttempts.ShouldBe(0);
    }

    [Fact]
    public async Task Counter_ExplicitReroute_ForgetsTheWire()
    {
        var scenario = await CounterCrossingScenario.LoadAsync(adaptive: false, sealedPinFixture: true);
        var unroutable = scenario.Manager.Connections.Single(c => c.IsBlockedFallback);
        await scenario.Canvas.RecalculateRoutesAsync();

        scenario.Manager.RequestReroute(unroutable);

        scenario.Manager.UnroutableWires.IsRemembered(unroutable).ShouldBeFalse();
        unroutable.RoutedPath.ShouldBeNull("the next pass searches it from scratch");
    }

    [Fact]
    public void Memory_ForgetsAWireOnceAPinMoves()
    {
        var (connection, start) = CreateConnection();
        var memory = new UnroutableWireMemory();
        memory.Remember(connection);
        memory.IsRemembered(connection).ShouldBeTrue();

        start.PhysicalX += 10;

        memory.IsRemembered(connection).ShouldBeFalse();
        memory.Count.ShouldBe(0);
    }

    [Fact]
    public void Memory_ChainSearchExhausted_OnlyForRememberedWires()
    {
        var (connection, _) = CreateConnection();
        var memory = new UnroutableWireMemory();

        memory.MarkChainSearchExhausted(connection);
        memory.IsChainSearchExhausted(connection).ShouldBeFalse();

        memory.Remember(connection);
        memory.MarkChainSearchExhausted(connection);
        memory.IsChainSearchExhausted(connection).ShouldBeTrue();

        memory.Forget(connection).ShouldBeTrue();
        memory.IsChainSearchExhausted(connection).ShouldBeFalse();
    }

    private static (WaveguideConnection Connection, Component Start) CreateConnection()
    {
        var start = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var end = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        end.PhysicalX = 600;
        var connection = new WaveguideConnection { StartPin = start.PhysicalPins[1], EndPin = end.PhysicalPins[0] };
        return (connection, start);
    }
}
