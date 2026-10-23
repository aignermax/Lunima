using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.Snapshots;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.Snapshots;

public sealed class ImportedRoutePublicationIdentityTests
{
    [Fact]
    public void UnchangedImportedPolygons_KeepEditorSourceAcrossRepeatedFrames()
    {
        var fixture = FrozenFixture();
        var source = Geometry();
        fixture.First.AttachAsDrawnGeometry(source);
        fixture.First.AsDrawnGeometry.ShouldBeSameAs(source);
        var frame = new RoutingFrame(new[] { fixture.First });

        frame.Prepare(fixture.Manager).Apply();
        frame.Prepare(fixture.Manager).Apply();

        fixture.First.AsDrawnGeometry.ShouldBeSameAs(source);
    }

    [Theory]
    [InlineData(0)] // Translation
    [InlineData(1)] // Layer
    [InlineData(2)] // Datatype
    public void ChangedImportedPolygons_ReplaceEditorSource(int change)
    {
        var fixture = FrozenFixture();
        var source = Geometry();
        var changed = change == 0 ? source.Translated(20, 30)
            : new AsDrawnGeometry(new[] { source.Polygons[0] with
            {
                Layer = change == 1 ? 12 : source.Polygons[0].Layer,
                DataType = change == 2 ? 8 : source.Polygons[0].DataType
            } });
        fixture.First.AttachAsDrawnGeometry(changed);
        var frame = new RoutingFrame(new[] { fixture.First });
        fixture.First.AttachAsDrawnGeometry(source);
        fixture.First.AsDrawnGeometry.ShouldBeSameAs(source);

        frame.Prepare(fixture.Manager).Apply();

        var published = fixture.First.AsDrawnGeometry.ShouldNotBeNull();
        ReferenceEquals(published, source).ShouldBeFalse();
        ReferenceEquals(published, changed).ShouldBeFalse("worker objects are never adopted by the editor");
        published.Polygons[0].Layer.ShouldBe(changed.Polygons[0].Layer);
        published.Polygons[0].DataType.ShouldBe(changed.Polygons[0].DataType);
        published.Polygons[0].Points.ShouldBe(changed.Polygons[0].Points);
    }

    [Fact]
    public void RestoredInitialResult_ReplacesAnIntermediateTranslation()
    {
        var fixture = FrozenFixture();
        var source = Geometry();
        fixture.First.AttachAsDrawnGeometry(source);
        fixture.First.AsDrawnGeometry.ShouldBeSameAs(source);
        var final = new RoutingFrame(new[] { fixture.First });
        var intermediate = source.Translated(10, 10);
        fixture.First.AttachAsDrawnGeometry(intermediate);

        final.Prepare(fixture.Manager).Apply();

        var restored = fixture.First.AsDrawnGeometry.ShouldNotBeNull();
        ReferenceEquals(restored, intermediate).ShouldBeFalse();
        restored.Polygons[0].Points.ShouldBe(source.Polygons[0].Points);
    }

    private static RoutingSnapshotFixture FrozenFixture()
    {
        var fixture = new RoutingSnapshotFixture();
        fixture.First.RestoreCachedPath(RoutingSnapshotFixture.Straight(fixture.First));
        fixture.First.IsRouteFrozen = true;
        return fixture;
    }

    private static AsDrawnGeometry Geometry() => new(new[]
    {
        new OutlinePolygon { Layer = 3, DataType = 2, Points = new[]
        {
            new OutlinePoint(0, 0), new OutlinePoint(10, 0),
            new OutlinePoint(10, 2), new OutlinePoint(0, 0)
        } }
    });
}
