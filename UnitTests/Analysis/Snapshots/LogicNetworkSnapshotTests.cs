using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;

namespace UnitTests.Analysis.Snapshots;

public sealed class LogicNetworkSnapshotTests
{
    [Fact]
    public async Task WorkerHeld_AllGatePhysicsAndPersistedAssignmentsAreCapturedTogether()
    {
        var first = LogicSnapshotFixture.Gate("first");
        var second = LogicSnapshotFixture.Gate("second");
        second.TruthTablePinAssignment!.OutputSignalNames = new() { ["y"] = "result" };
        var assembler = new LogicSnapshotFixture.HeldAssembler();
        var running = assembler.AssembleAsync([first, second], [LogicSnapshotFixture.Connect(first, second)], 1550);
        var network = await assembler.Barrier.MutateThenFinishAsync(running, () =>
        {
            LogicSnapshotFixture.RemovePhysics(second);
            second.TruthTablePinAssignment.InputPinNames.Clear();
            second.TruthTablePinAssignment.OutputSignalNames["y"] = "changed";
            second.TruthTablePinAssignment.IsRegister = true;
        });
        network.InputPinNames.ShouldBe(["first.a", "first.b", "second.b"]);
        network.OutputPinNames.ShouldBe(["first.y", "result"]);
        network.Evaluate(new Dictionary<string, bool>
        {
            ["first.a"] = true, ["first.b"] = false, ["second.b"] = false
        })["result"].ShouldBeTrue();
    }

    [Fact]
    public async Task WorkerHeld_ConnectionEndpointsAndCollectionsCannotRewireCapturedBuild()
    {
        var first = LogicSnapshotFixture.Gate("first");
        var second = LogicSnapshotFixture.Gate("second");
        var wire = LogicSnapshotFixture.Connect(first, second);
        var connections = new List<CAP_Core.Components.Connections.WaveguideConnection> { wire };
        var components = new List<Component> { first, second };
        var assembler = new LogicSnapshotFixture.HeldAssembler();
        var running = assembler.AssembleAsync(components, connections, 1550);
        var network = await assembler.Barrier.MutateThenFinishAsync(running, () =>
        {
            wire.EndPin = second.PhysicalPins.Single(pin => pin.Name == "b");
            connections.Clear();
            components.Clear();
        });
        network.InputPinNames.ShouldBe(["first.a", "first.b", "second.b"]);
        network.Evaluate(new Dictionary<string, bool>
        {
            ["first.a"] = true, ["first.b"] = false, ["second.b"] = false
        })["second.y"].ShouldBeTrue();
    }

    [Fact]
    public async Task WorkerHeld_GeometryAndMaterialChangesCannotAlterCapturedTiming()
    {
        var first = LogicSnapshotFixture.Gate("first");
        var second = LogicSnapshotFixture.Gate("second");
        var wire = LogicSnapshotFixture.Connect(first, second);
        var gateDelay = new GateDelayCalculator().CalculatePicoseconds(first, 1550);
        var wireDelay = new WireDelayCalculator().CalculatePicoseconds(wire, 1550);
        var assembler = new LogicSnapshotFixture.HeldAssembler();
        var running = assembler.AssembleAsync([first, second], [wire], 1550);
        var network = await assembler.Barrier.MutateThenFinishAsync(running, () =>
        {
            first.ChildComponents[0].WidthMicrometers *= 20;
            var replacement = new RoutedPath();
            replacement.Segments.Add(new StraightSegment(0, 0, 10000, 0, 0));
            wire.RestoreCachedPath(replacement);
        });
        network.GateDelaysPicoseconds["first"].ShouldBe(gateDelay, 1e-9);
        network.WireDelaysPicoseconds.Single().Value.ShouldBe(wireDelay, 1e-9);
    }

    [Fact]
    public async Task WorkerHeld_NestedGateNamesAndFrozenWiringRemainStable()
    {
        var first = LogicSnapshotFixture.Gate("first");
        var second = LogicSnapshotFixture.Gate("second");
        var cell = new ComponentGroup("cell");
        cell.AddChild(first);
        cell.AddChild(second);
        var route = new RoutedPath();
        route.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));
        cell.AddInternalPath(new FrozenWaveguidePath
        {
            StartPin = first.ExternalPins.Single(pin => pin.Name == "y").InternalPin,
            EndPin = second.ExternalPins.Single(pin => pin.Name == "a").InternalPin,
            Path = route
        });
        var assembler = new LogicSnapshotFixture.HeldAssembler();
        var running = assembler.AssembleAsync([cell], [], 1550);
        var network = await assembler.Barrier.MutateThenFinishAsync(running, () =>
        {
            cell.GroupName = "renamed";
            cell.InternalPaths.Clear();
            first.TruthTablePinAssignment = null;
        });
        network.Gates.Keys.ShouldBe(["cell/first", "cell/second"]);
        network.InputPinNames.ShouldBe(["cell/first.a", "cell/first.b", "cell/second.b"]);
    }
}
