using Shouldly;
using Xunit;

namespace UnitTests.Routing.CrossingInsertion;

/// <summary>
/// With adaptive crossing insertion on, a move that makes crossing chains stale must not make
/// the design worse: only the dissolved wires are re-routed (no ordering cascade over the
/// whole design), crossings come back only where a wire needs one, and no wire that was fine
/// is pushed through new crossings to make room. Before this, the counter move re-routed
/// everything five times and ended with 13 crossings and 5 blocked wires.
/// </summary>
public class AdaptiveCrossingRerouteTests
{
    /// <summary>
    /// The moved wire has to swap sides with its 4 µm fan-out neighbour after the move; the only
    /// connection the search finds pushes that neighbour through six new crossings, which is
    /// refused, so it stays visibly blocked next to the one wire the counter never could route.
    /// </summary>
    private const int MaxBlockedAfterMove = 2;

    [Fact]
    [Trait("Category", "Slow")]
    public async Task CounterMove_ReroutesOnlyTheDissolvedWires_WithoutAddingCrossings()
    {
        var scenario = await CounterCrossingScenario.LoadAsync(adaptive: true, sealedPinFixture: true);
        int crossingsBefore = scenario.Crossings().Count;

        await scenario.MoveAndRouteAsync(scenario.GroupToMove());

        scenario.Manager.LastRoutingPassTimings!.OrderingAttempts.ShouldBe(0,
            "dissolved wires are re-routed on their own, not by re-routing the whole design");
        scenario.Crossings().Count.ShouldBeLessThanOrEqualTo(crossingsBefore);
        scenario.BlockedCount().ShouldBeLessThanOrEqualTo(MaxBlockedAfterMove);
        scenario.Canvas.Connections.Select(vm => vm.Connection)
            .ShouldBe(scenario.Manager.Connections, ignoreOrder: true);
    }
}
