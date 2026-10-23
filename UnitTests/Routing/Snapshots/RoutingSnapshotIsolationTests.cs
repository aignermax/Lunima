using CAP_Core.Components.Core;
using CAP_Core.LightCalculation.MaterialDispersion;
using CAP_Core.Routing.Snapshots;
using Shouldly;

namespace UnitTests.Routing.Snapshots;

public sealed class RoutingSnapshotIsolationTests
{
    [Fact]
    public void Capture_PreservesIdentityAndOrderWithoutSharingGeometryOrInstances()
    {
        var fixture = new RoutingSnapshotFixture();
        var original = fixture.First;
        original.RestoreCachedPath(RoutingSnapshotFixture.Straight(original));
        original.IsRouteFrozen = true;
        original.StraightShiftOffsets[0] = 3;
        original.BendRadiusOverrides[1] = 22;
        var originalStart = original.StartPin.GetAbsolutePosition();
        var ids = fixture.Manager.Connections.Select(connection => connection.Id).ToArray();
        var snapshot = RoutingInputSnapshot.Capture(fixture.Components, fixture.Router, fixture.Manager);
        original.RoutedPath!.Segments.Clear();
        original.StartPin.ParentComponent.PhysicalX = 900;
        original.StraightShiftOffsets.Clear();
        fixture.Router.AllowedBendRadii.Clear();

        var first = snapshot.CreatePrivateGraph();
        var second = snapshot.CreatePrivateGraph();
        first.Components.Select(component => component.Id).ShouldBe(fixture.Components.Select(component => component.Id));
        first.Manager.Connections.Select(connection => connection.Id).ShouldBe(ids);
        var copy = first.Manager.Connections[0];
        copy.ShouldNotBeSameAs(original);
        copy.StartPin.ShouldNotBeSameAs(original.StartPin);
        copy.StartPin.ParentComponent.ShouldNotBeSameAs(original.StartPin.ParentComponent);
        copy.StartPin.PinId.ShouldBe(original.StartPin.PinId);
        copy.StartPin.LogicalPin.IDOutFlow.ShouldBe(original.StartPin.LogicalPin.IDOutFlow);
        copy.StartPin.GetAbsolutePosition().ShouldBe(originalStart);
        copy.StraightShiftOffsets[0].ShouldBe(3);
        copy.BendRadiusOverrides[1].ShouldBe(22);
        copy.RoutedPath!.Segments.Count.ShouldBe(1);
        first.Router.AllowedBendRadii.ShouldNotBeEmpty();
        copy.RoutedPath.Segments.Clear();
        second.Manager.Connections[0].RoutedPath!.Segments.Count.ShouldBe(1);
    }

    [Fact]
    public void Capture_ResolvesPhysicsAndProcessFloorsBeforeCallerCallbacksArePoisoned()
    {
        var fixture = new RoutingSnapshotFixture();
        var model = new ControlledDispersion();
        fixture.First.DispersionModel = model;
        var readsAllowed = true;
        fixture.Router.ConnectionProcessFloorProvider = (_, _) => readsAllowed
            ? 27 : throw new InvalidOperationException("live process provider escaped capture");
        var electrical = fixture.Manager.Connections[1];
        electrical.StartPin.LogicalPin.MatterType = MatterType.Electricity;
        electrical.EndPin.LogicalPin.MatterType = MatterType.Electricity;
        var snapshot = RoutingInputSnapshot.Capture(fixture.Components, fixture.Router, fixture.Manager);
        readsAllowed = model.ReadsAllowed = false;

        var graph = snapshot.CreatePrivateGraph();
        var opticalCopy = graph.Manager.Connections[0];
        var electricalCopy = graph.Manager.Connections[1];
        graph.Router.ResolveProcessFloorFor(opticalCopy.StartPin, opticalCopy.EndPin).ShouldBe(27);
        graph.Router.ResolveProcessFloorFor(electricalCopy.StartPin, electricalCopy.EndPin).ShouldBe(45);
        opticalCopy.RestoreCachedPath(RoutingSnapshotFixture.Straight(opticalCopy));
        opticalCopy.TotalLossDb.ShouldBe(19.0 * opticalCopy.PathLengthMicrometers / 10000, 1e-10);
        graph.Manager.MetalTraceWidthMicrometers.ShouldBe(8);
    }

    private sealed class ControlledDispersion : IDispersionModel
    {
        internal bool ReadsAllowed { get; set; } = true;
        public double NEffAt(double wavelengthNm) => Read(2.7);
        public double GroupIndexAt(double wavelengthNm) => Read(3.2);
        public double LossDbPerCmAt(double wavelengthNm) => Read(19);
        private double Read(double value) => ReadsAllowed ? value
            : throw new InvalidOperationException("live dispersion escaped capture");
    }
}
