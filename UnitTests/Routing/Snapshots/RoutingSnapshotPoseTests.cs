using CAP_Core.Components.Core;
using CAP_Core.Routing.Snapshots;
using Shouldly;

namespace UnitTests.Routing.Snapshots;

public sealed class RoutingSnapshotPoseTests
{
    [Theory]
    [InlineData(90, false)]
    [InlineData(37, true)]
    [InlineData(270, true)]
    public void TransformedNestedExternalPorts_KeepWorldPoseLogicalIdentityAndPrivateHierarchy(double degrees, bool mirrored)
    {
        var fixture = new RoutingSnapshotFixture();
        var child = fixture.Components[2];
        child.WidthMicrometers = 24;
        child.HeightMicrometers = 11;
        child.PhysicalPins[1].OffsetXMicrometers = 24;
        child.PhysicalPins[1].OffsetYMicrometers = 3;
        child.PhysicalPins[1].AngleDegrees = 25;
        if (mirrored) ComponentPoseTransform.MirrorPinsHorizontally(child);
        ComponentPoseTransform.RotateByDegrees(child, degrees);
        var inner = new ComponentGroup("inner");
        inner.AddChild(child);
        inner.AddExternalPin(new GroupPin
        {
            Name = "exposed", InternalPin = child.PhysicalPins[1],
            RelativeX = 3, RelativeY = 7, AngleDegrees = 35
        });
        inner.SyncPhysicalPinsFromExternalPins();
        if (mirrored) ComponentPoseTransform.MirrorPinsHorizontally(inner);
        ComponentPoseTransform.RotateByDegrees(inner, degrees);
        var outer = new ComponentGroup("outer");
        outer.AddChild(inner);
        var roots = fixture.Components.Where(component => component != child).Append(outer).ToArray();
        var expectedChild = child.PhysicalPins.Select(Pose).ToArray();
        var expectedGroup = inner.PhysicalPins.Select(Pose).ToArray();
        var snapshot = RoutingInputSnapshot.Capture(roots, fixture.Router, fixture.Manager);
        child.PhysicalX += 500;
        inner.ExternalPins[0].RelativeX += 50;

        var graph = snapshot.CreatePrivateGraph();
        var copiedOuter = graph.Components.OfType<ComponentGroup>().Single();
        var copiedInner = copiedOuter.ChildComponents.Single().ShouldBeOfType<ComponentGroup>();
        var copiedChild = copiedInner.ChildComponents.Single();
        copiedChild.PhysicalPins.Select(Pose).ShouldBe(expectedChild);
        copiedInner.PhysicalPins.Select(Pose).ShouldBe(expectedGroup);
        copiedChild.IsMirroredHorizontally.ShouldBe(mirrored);
        copiedChild.UnrotatedWidthMicrometers.ShouldBe(24);
        copiedChild.UnrotatedHeightMicrometers.ShouldBe(11);
        copiedInner.ExternalPins.Single().RelativeX.ShouldBe(3);
        copiedInner.ExternalPins.Single().InternalPin.ShouldBeSameAs(copiedChild.PhysicalPins[1]);
        copiedInner.PhysicalPins.Single().LogicalPin.ShouldBeSameAs(copiedChild.PhysicalPins[1].LogicalPin);
        graph.Manager.Connections[0].StartPin.ShouldBeSameAs(copiedChild.PhysicalPins[1]);
        copiedChild.ParentGroup.ShouldBeSameAs(copiedInner);
        copiedInner.ParentGroup.ShouldBeSameAs(copiedOuter);
    }

    private static (Guid Id, (double x, double y) Position, double Angle, Guid In, Guid Out) Pose(PhysicalPin pin) =>
        (pin.PinId, pin.GetAbsolutePosition(), pin.GetAbsoluteAngle(), pin.LogicalPin.IDInFlow, pin.LogicalPin.IDOutFlow);
}
