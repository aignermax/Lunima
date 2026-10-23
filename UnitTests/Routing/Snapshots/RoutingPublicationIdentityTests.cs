using CAP_Core.Routing.Snapshots;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.Snapshots;

public sealed class RoutingPublicationIdentityTests
{
    [Fact]
    public void UnchangedPrivatePath_PreservesOriginalEditorIdentity()
    {
        var fixture = FrozenFixture();
        var original = fixture.First.RoutedPath;
        var references = RoutingPassResult.CaptureOriginalPaths(fixture.Manager);
        var input = RoutingInputSnapshot.Capture(fixture.Components, fixture.Router, fixture.Manager, false);

        var result = RoutingSnapshotWorker.Run(input, new(), default);
        result.ShouldNotBeNull();
        result.Prepare(fixture.Components, fixture.Manager, fixture.Router, references).Apply();

        fixture.First.RoutedPath.ShouldBeSameAs(original);
    }

    [Fact]
    public void UnchangedPrivatePath_AfterIntermediatePublicationRestoresFinalGeometry()
    {
        var fixture = FrozenFixture();
        var expected = fixture.First.RoutedPath!.Segments[0].StartPoint;
        var references = RoutingPassResult.CaptureOriginalPaths(fixture.Manager);
        var input = RoutingInputSnapshot.Capture(fixture.Components, fixture.Router, fixture.Manager, false);
        var result = RoutingSnapshotWorker.Run(input, new(), default);
        // Models an accepted progressive frame followed by a private routing rollback.
        var intermediate = RoutingSnapshotFixture.Straight(fixture.First, 25);
        fixture.First.RestoreCachedPath(intermediate);

        result.ShouldNotBeNull();
        result.Prepare(fixture.Components, fixture.Manager, fixture.Router, references).Apply();

        ReferenceEquals(fixture.First.RoutedPath, intermediate).ShouldBeFalse();
        fixture.First.RoutedPath!.Segments[0].StartPoint.ShouldBe(expected);
    }

    [Fact]
    public void RecalculatedPrivatePath_PublishesNewGeometry()
    {
        var fixture = FrozenFixture();
        fixture.First.IsRouteFrozen = false;
        fixture.Manager.RequestReroute(fixture.First);
        var references = RoutingPassResult.CaptureOriginalPaths(fixture.Manager);
        references[fixture.First.Id].ShouldBeNull();
        var input = RoutingInputSnapshot.Capture(fixture.Components, fixture.Router, fixture.Manager, false);

        var result = RoutingSnapshotWorker.Run(input, new(), default);
        result.ShouldNotBeNull();
        result.Prepare(fixture.Components, fixture.Manager, fixture.Router, references).Apply();

        fixture.First.RoutedPath.ShouldNotBeNull();
        fixture.First.RoutedPath.Segments.ShouldNotBeEmpty();
    }

    private static RoutingSnapshotFixture FrozenFixture()
    {
        var fixture = new RoutingSnapshotFixture();
        foreach (var wire in fixture.Manager.Connections)
        {
            wire.RestoreCachedPath(RoutingSnapshotFixture.Straight(wire));
            wire.IsRouteFrozen = true;
        }
        return fixture;
    }
}
