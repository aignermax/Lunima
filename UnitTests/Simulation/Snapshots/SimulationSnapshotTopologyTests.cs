using System.Numerics;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation.MaterialDispersion;
using CAP_Core.LightCalculation.Snapshots;
using CAP_Core.Routing;
using Shouldly;

namespace UnitTests.Simulation.Snapshots;

public sealed class SimulationSnapshotTopologyTests
{
    [Fact]
    public async Task NestedGroupPaths_AreCapturedWithoutComputingOrChangingLiveGroupMatrices()
    {
        var first = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var second = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var group = new ComponentGroup("inner");
        group.AddChild(first);
        group.AddChild(second);
        var path = new FrozenWaveguidePath { StartPin = first.PhysicalPins[1], EndPin = second.PhysicalPins[0] };
        group.AddInternalPath(path);
        group.AddExternalPin(new GroupPin { Name = "in", InternalPin = first.PhysicalPins[0] });
        var outer = new ComponentGroup("outer");
        outer.AddChild(group);
        var flowId = second.PhysicalPins[1].LogicalPin.IDOutFlow;
        var snapshot = SimulationSnapshotCapture.Capture([outer], [],
            [new(first.PhysicalPins[0].LogicalPin.IDInFlow, 1550, Complex.One)], false);
        group.WaveLengthToSMatrixMap.ShouldBeEmpty();
        group.PhysicalPins.ShouldBeEmpty("capture must not call EnsureSMatrixComputed on the live group");
        group.InternalPaths.Clear();
        group.ChildComponents.Clear();

        var result = await SimulationSnapshotSolver.RunAsync(snapshot).WaitAsync(TimeSpan.FromSeconds(5));
        result.Fields[flowId].ShouldBe(Complex.One);
        snapshot.ComponentIds.ShouldContain(outer.Id);
        snapshot.ComponentIds.ShouldContain(group.Id);
        snapshot.ComponentIds.ShouldContain(first.Id);
        snapshot.ConnectionIds.ShouldContain(path.PathId);
        group.WaveLengthToSMatrixMap.ShouldBeEmpty();
    }

    [Fact]
    public async Task CoherentRouteLossAndDispersion_AreDetachedBeforeWorkerReads()
    {
        var first = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var second = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var model = new MutableDispersion();
        var connection = new WaveguideConnection
            { StartPin = first.PhysicalPins[1], EndPin = second.PhysicalPins[0], DispersionModel = model };
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));
        connection.RestoreCachedPath(path);
        var expected = connection.GetCoherentTransmission(1550);
        var input = first.PhysicalPins[0].LogicalPin.IDInFlow;
        var output = second.PhysicalPins[1].LogicalPin.IDOutFlow;
        var snapshot = SimulationSnapshotCapture.Capture([first, second], [connection],
            [new(input, 1550, Complex.One)], true);
        model.ForbidReads = true;
        path.Segments.Clear();
        connection.StartPin = second.PhysicalPins[0];

        var result = await Task.Run(() => SimulationSnapshotSolver.RunAsync(snapshot)).WaitAsync(TimeSpan.FromSeconds(5));
        (result.Fields[output] - expected).Magnitude.ShouldBeLessThan(1e-10);
        snapshot.ConnectionIds.ShouldContain(connection.Id);
    }

    private sealed class MutableDispersion : IDispersionModel
    {
        public bool ForbidReads { get; set; }
        public double NEffAt(double wavelengthNm) => ForbidReads
            ? throw new InvalidOperationException("live dispersion escaped capture") : 3.1;
        public double GroupIndexAt(double wavelengthNm) => 3.1;
        public double LossDbPerCmAt(double wavelengthNm) => 12;
    }
}
