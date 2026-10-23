using CAP_Core.Routing;
using CAP_Core.Routing.Snapshots;
using Shouldly;

namespace UnitTests.Routing.Snapshots;

public sealed class RoutingSnapshotParityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrivateRouting_MatchesExistingManagerGeometryLossAndOrder(bool sequential)
    {
        var fixture = new RoutingSnapshotFixture(sequential);
        fixture.First.PropagationLossDbPerCm = 4;
        fixture.First.ExcessBendLossDbPer90Deg = 0.15;
        var snapshot = RoutingInputSnapshot.Capture(fixture.Components, fixture.Router, fixture.Manager);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var graph = snapshot.CreatePrivateGraph();
        await Task.Run(() =>
        {
            fixture.Manager.RecalculateAllTransmissions(cancellationToken: stop.Token);
            graph.Route(stop.Token);
        }).WaitAsync(TimeSpan.FromSeconds(10));
        stop.Token.ThrowIfCancellationRequested();

        graph.Manager.Connections.Select(connection => connection.Id)
            .ShouldBe(fixture.Manager.Connections.Select(connection => connection.Id));
        foreach (var original in fixture.Manager.Connections)
        {
            var copy = graph.Manager.Connections.Single(connection => connection.Id == original.Id);
            copy.RoutedPath.ShouldNotBeNull();
            original.RoutedPath.ShouldNotBeNull();
            copy.RoutedPath.IsBlockedFallback.ShouldBe(original.RoutedPath.IsBlockedFallback);
            copy.RoutedPath.FailureReason.ShouldBe(original.RoutedPath.FailureReason);
            copy.PathLengthMicrometers.ShouldBe(original.PathLengthMicrometers, 1e-9);
            copy.TransmissionCoefficient.ShouldBe(original.TransmissionCoefficient);
            copy.RoutedPath.Segments.Select(Describe).ShouldBe(original.RoutedPath.Segments.Select(Describe));
        }
    }

    [Fact]
    public async Task FailedWireMemory_SurvivesPrivateGraphRebuildAndNextPassWithoutSharingState()
    {
        var fixture = new RoutingSnapshotFixture();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        // Consume the existing wire-set-change flag before remembering a failure.
        await Task.Run(() => fixture.Manager.RecalculateAllTransmissions(cancellationToken: stop.Token))
            .WaitAsync(TimeSpan.FromSeconds(10));
        var blocked = RoutingSnapshotFixture.Straight(fixture.First);
        blocked.IsBlockedFallback = true;
        fixture.First.RestoreCachedPath(blocked);
        fixture.Manager.UnroutableWires.Remember(fixture.First);
        fixture.Manager.UnroutableWires.MarkChainSearchExhausted(fixture.First);
        var snapshot = RoutingInputSnapshot.Capture(fixture.Components, fixture.Router, fixture.Manager);
        fixture.Manager.UnroutableWires.Clear();

        var graph = snapshot.CreatePrivateGraph();
        var copy = graph.Manager.Connections[0];
        graph.Manager.UnroutableWires.IsRemembered(copy).ShouldBeTrue();
        graph.Manager.UnroutableWires.IsChainSearchExhausted(copy).ShouldBeTrue();
        await Task.Run(() => graph.Route(stop.Token)).WaitAsync(TimeSpan.FromSeconds(10));
        stop.Token.ThrowIfCancellationRequested();
        graph.Manager.UnroutableWires.IsRemembered(copy).ShouldBeTrue(
            "reconstructing private connections must not synthesize a wire-set edit that clears captured memory");
        copy.IsBlockedFallback.ShouldBeTrue();
        fixture.Manager.UnroutableWires.Count.ShouldBe(0);
    }

    private static string Describe(PathSegment segment) => segment is BendSegment bend
        ? FormattableString.Invariant($"bend:{bend.Center}:{bend.RadiusMicrometers}:{bend.StartAngleDegrees}:{bend.SweepAngleDegrees}")
        : FormattableString.Invariant($"straight:{segment.StartPoint}:{segment.EndPoint}:{segment.StartAngleDegrees}:{segment.EndAngleDegrees}");
}
