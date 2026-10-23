using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.Snapshots;
using Shouldly;

namespace UnitTests.Routing.Snapshots;

public sealed class RoutingSnapshotGeometryTests
{
    [Fact]
    public void ImportedGeometry_PreservesHiddenOriginalBindingAndCopiesCallerOwnedPolygonLists()
    {
        var fixture = new RoutingSnapshotFixture();
        var wire = fixture.First;
        var originalPath = RoutingSnapshotFixture.Straight(wire);
        var points = new List<OutlinePoint> { new(10, 104), new(200, 104), new(200, 106), new(10, 104) };
        var polygons = new List<OutlinePolygon> { new() { Layer = 7, DataType = 3, Points = points } };
        wire.RestoreCachedPath(originalPath);
        wire.IsRouteFrozen = true;
        wire.AttachAsDrawnGeometry(new AsDrawnGeometry(polygons));
        wire.RestoreCachedPath(RoutingSnapshotFixture.Straight(wire, 20));
        wire.AsDrawnGeometry.ShouldBeNull("the attached polygons describe the original, not current route");
        var snapshot = RoutingInputSnapshot.Capture(fixture.Components, fixture.Router, fixture.Manager);
        points.Clear();
        polygons.Clear();

        var copy = snapshot.CreatePrivateGraph().Manager.Connections[0];
        copy.AsDrawnGeometry.ShouldBeNull("re-attaching polygons to the current route would invent a new binding");
        copy.AttachedAsDrawnGeometry.ShouldNotBeNull();
        copy.RestoreCachedPath(originalPath.DeepCopy());
        copy.AsDrawnGeometry.ShouldNotBeNull("moving back to the captured original must restore its polygons");
        copy.AsDrawnGeometry.Polygons.Single().Layer.ShouldBe(7);
        copy.AsDrawnGeometry.Polygons.Single().DataType.ShouldBe(3);
        copy.AsDrawnGeometry.Polygons.Single().Points.Count.ShouldBe(4);
        copy.AsDrawnGeometry.Polygons.Single().Points[0].ShouldBe(new OutlinePoint(10, 104));
    }

    [Fact]
    public void NestedGroupPinsAndFrozenObstacles_ArePrivateAndKeepOriginalIdentities()
    {
        var fixture = new RoutingSnapshotFixture();
        var inner = new ComponentGroup("inner");
        foreach (var component in fixture.Components) inner.AddChild(component);
        var frozen = new FrozenWaveguidePath
        {
            StartPin = fixture.First.StartPin,
            EndPin = fixture.First.EndPin,
            Path = RoutingSnapshotFixture.Straight(fixture.First)
        };
        inner.AddInternalPath(frozen);
        var outer = new ComponentGroup("outer");
        outer.AddChild(inner);
        var snapshot = RoutingInputSnapshot.Capture([outer], fixture.Router, fixture.Manager);
        inner.InternalPaths.Clear();
        inner.ChildComponents.Clear();

        var graph = snapshot.CreatePrivateGraph();
        var outerCopy = graph.Components.Single().ShouldBeOfType<ComponentGroup>();
        var innerCopy = outerCopy.ChildComponents.Single().ShouldBeOfType<ComponentGroup>();
        outerCopy.Id.ShouldBe(outer.Id);
        innerCopy.Id.ShouldBe(inner.Id);
        innerCopy.InternalPaths.Single().PathId.ShouldBe(frozen.PathId);
        innerCopy.InternalPaths.Single().Path.ShouldNotBeSameAs(frozen.Path);
        graph.Manager.Connections[0].StartPin.ParentComponent.ParentGroup.ShouldBeSameAs(innerCopy);
    }
}
