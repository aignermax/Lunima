using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using Shouldly;

namespace UnitTests.Analysis.Snapshots;

public sealed class LayoutCompressionSnapshotTests
{
    [Fact]
    public void Solve_KeepsCapturedGeometryAndConnectivityWithoutMutatingLiveComponents()
    {
        var first = TestComponentFactory.CreateBasicComponent();
        var second = TestComponentFactory.CreateBasicComponent();
        first.PhysicalX = 300;
        first.PhysicalY = 400;
        second.PhysicalX = 1200;
        second.PhysicalY = 1000;
        List<Component> components = [first, second];
        var connection = TestComponentFactory.CreateConnection(first, second);
        var snapshot = LayoutCompressionSnapshot.Capture(components, [connection]);
        var expected = snapshot.Solve(default);
        first.PhysicalX.ShouldBe(300);
        second.PhysicalX.ShouldBe(1200);
        first.PhysicalX = -100;
        first.WidthMicrometers = 9000;
        second.IsLocked = true;
        connection.StartPin.ParentComponent = second;

        snapshot.Solve(default).Positions.ShouldBe(expected.Positions);
        first.PhysicalX.ShouldBe(-100);
        second.PhysicalX.ShouldBe(1200);
    }

    [Fact]
    public void CancelledSnapshot_DoesNotStartCalculation()
    {
        var snapshot = LayoutCompressionSnapshot.Capture([TestComponentFactory.CreateBasicComponent()], []);
        Should.Throw<OperationCanceledException>(() => snapshot.Solve(new CancellationToken(true)));
    }

    [Fact]
    public void CompatibilityCallback_CanStillChangePositionAndLockBeforeCentering()
    {
        var component = TestComponentFactory.CreateBasicComponent();
        component.PhysicalX = 900;
        var result = new LayoutCompressor().CompressLayout([component], [], (_, _) =>
        {
            component.PhysicalX = 123;
            component.IsLocked = true;
        });
        result[component].X.ShouldBe(123);
        component.PhysicalX.ShouldBe(123);
    }
}
