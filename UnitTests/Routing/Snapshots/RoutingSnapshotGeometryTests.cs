using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.Snapshots;
using Shouldly;

namespace UnitTests.Routing.Snapshots;

public sealed class RoutingSnapshotGeometryTests
{
    [Fact]
    public void LargePrivateHierarchy_KeepsChildAndPathOrderEndpointIdentityAndCapturedBounds()
    {
        var children = Enumerable.Range(0, 128).Select(index =>
        {
            var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
            component.PhysicalX = index * 25;
            component.PhysicalY = index % 3 * 40;
            return component;
        }).ToArray();
        var paths = children.Zip(children.Skip(1), (first, second) => new FrozenWaveguidePath
        {
            StartPin = first.PhysicalPins[1], EndPin = second.PhysicalPins[0],
            Path = RoutingSnapshotFixture.Straight(new WaveguideConnection
                { StartPin = first.PhysicalPins[1], EndPin = second.PhysicalPins[0] })
        }).ToArray();
        var group = new ComponentGroup("many children");
        group.AddChildren(children);
        group.AddInternalPaths(paths);
        group.PhysicalX = -13;
        group.PhysicalY = -17;
        group.WidthMicrometers = 4000;
        group.HeightMicrometers = 500;
        var router = new CAP_Core.Routing.WaveguideRouter();
        var snapshot = RoutingInputSnapshot.Capture([group], router, new WaveguideConnectionManager(router));

        var copy = snapshot.CreatePrivateGraph().Components.Single().ShouldBeOfType<ComponentGroup>();

        copy.ChildComponents.Select(child => child.Id).ShouldBe(children.Select(child => child.Id));
        copy.InternalPaths.Select(path => path.PathId).ShouldBe(paths.Select(path => path.PathId));
        (copy.PhysicalX, copy.PhysicalY, copy.WidthMicrometers, copy.HeightMicrometers).ShouldBe((-13d, -17d, 4000d, 500d));
        for (var index = 0; index < paths.Length; index++)
        {
            copy.InternalPaths[index].StartPin.ShouldBeSameAs(copy.ChildComponents[index].PhysicalPins[1]);
            copy.InternalPaths[index].EndPin.ShouldBeSameAs(copy.ChildComponents[index + 1].PhysicalPins[0]);
            copy.ChildComponents[index].ParentGroup.ShouldBeSameAs(copy);
        }
    }

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
