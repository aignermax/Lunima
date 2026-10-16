using Shouldly;
using Xunit;

namespace UnitTests.Routing.CrossingInsertion;

/// <summary>The shipped counter settles without repeatedly searching its unchanged wires.</summary>
public class CounterUnchangedRoutingTests
{
    [Fact]
    public async Task SettledCounter_UnchangedPassKeepsRoutesWithoutSearchOrOrderingRetries()
    {
        var scenario = await CounterCrossingScenario.LoadAsync(adaptive: false);
        // Allow post-load bend optimization to settle before checking route reuse.
        await scenario.Canvas.RecalculateRoutesAsync();
        var before = scenario.Manager.Connections.ToDictionary(c => c, c => c.RoutedPath);

        await scenario.Canvas.RecalculateRoutesAsync();

        scenario.Canvas.Routing.RoutedConnectionCount.ShouldBe(0);
        scenario.Manager.LastRoutingPassTimings!.OrderingAttempts.ShouldBe(0);
        scenario.Manager.Connections.ShouldBe(before.Keys, ignoreOrder: true);
        foreach (var connection in scenario.Manager.Connections)
            connection.RoutedPath.ShouldBeSameAs(before[connection],
                "an unchanged settled route must retain its exact geometry");

        var router = scenario.Canvas.Router;
        router.PathfindingGrid!.RebuildFromComponents(scenario.Canvas.Components.Select(vm => vm.Component));
        foreach (var connection in scenario.Manager.Connections.Where(c => !c.IsBlockedFallback))
        {
            connection.RoutedPath.ShouldNotBeNull();
            router.IsPathBlockedByComponents(connection.RoutedPath!.Segments,
                new[] { connection.StartPin, connection.EndPin }).ShouldBeFalse(
                $"accepted route {connection.Id} must pass the same conservative component check next time");
        }
    }
}
