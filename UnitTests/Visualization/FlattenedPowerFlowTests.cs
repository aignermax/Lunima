using System.Numerics;
using CAP.Avalonia.Visualization;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using Shouldly;
using static UnitTests.Simulation.Snapshots.SnapshotPhysicsOracle;

namespace UnitTests.Visualization;

public sealed class FlattenedPowerFlowTests
{
    [Fact]
    public void CompleteNestedFields_DoNotReadOrSolveLiveComponentMatrices()
    {
        var (group, path, fields) = CreateGroup();
        var child = group.ChildComponents[0];
        // A singular live matrix makes any redundant group solve observable deterministically.
        // Complete flattened results already include explicit zero fields, not just lit pins.
        var pin = child.PhysicalPins[0].LogicalPin;
        child.WaveLengthToSMatrixMap[1550].SetValues(new()
            { [(pin.IDInFlow, pin.IDInFlow)] = Complex.One });
        var outer = new ComponentGroup("outer");
        outer.AddChild(group);
        var visualizer = new PowerFlowVisualizer();
        Should.Throw<NonConvergentCircuitException>(() => visualizer.UpdateFromSimulation([], [group], fields));

        var prepared = visualizer.PrepareFromFlattenedSimulation([], [outer], fields);
        prepared.ConnectionFlows[path.PathId].AveragePower.ShouldBe(0.25, 1e-9);
        group.WaveLengthToSMatrixMap.ShouldBeEmpty();
        outer.WaveLengthToSMatrixMap.ShouldBeEmpty();
    }

    [Fact]
    public void MissingEvenZeroDirection_RejectsFlattenedContractInsteadOfGuessingOrRecomputing()
    {
        var (group, path, fields) = CreateGroup();
        fields.Remove(path.StartPin.LogicalPin.IDInFlow).ShouldBeTrue();
        var visualizer = new PowerFlowVisualizer();

        Should.Throw<InvalidDataException>(() => visualizer.PrepareFromFlattenedSimulation([], [group], fields));
        visualizer.CurrentResult.ShouldBeNull();
    }

    [Fact]
    public void Preparation_DoesNotReplaceCurrentOverlayUntilExplicitPublication()
    {
        var (group, _, fields) = CreateGroup();
        var visualizer = new PowerFlowVisualizer();
        visualizer.UpdateFromSimulation([], [], new Dictionary<Guid, Complex>());
        var old = visualizer.CurrentResult;

        var prepared = visualizer.PrepareFromFlattenedSimulation([], [group], fields);
        visualizer.CurrentResult.ShouldBeSameAs(old);
        visualizer.PublishPreparedResult(prepared);
        visualizer.CurrentResult.ShouldBeSameAs(prepared);
    }

    [Fact]
    public void LegacyCollapsedFields_StillComputeMissingInternalFlow()
    {
        var (group, path, _) = CreateGroup();
        var first = group.ChildComponents[0];
        group.AddExternalPin(new GroupPin { Name = "input", InternalPin = first.PhysicalPins[0] });
        var visualizer = new PowerFlowVisualizer();

        visualizer.UpdateFromSimulation([], [group], new Dictionary<Guid, Complex>
            { [first.PhysicalPins[0].LogicalPin.IDInFlow] = new(0.5, 0) });
        visualizer.CurrentResult!.ConnectionFlows[path.PathId].AveragePower.ShouldBe(0.25, 1e-9);
    }

    private static (ComponentGroup Group, FrozenWaveguidePath Path, Dictionary<Guid, Complex> Fields) CreateGroup()
    {
        var first = CreateComponent(2, (0, 1, Complex.One));
        var second = CreateComponent(2, (0, 1, Complex.One));
        var group = new ComponentGroup("inner");
        group.AddChild(first);
        group.AddChild(second);
        var path = new FrozenWaveguidePath { StartPin = first.PhysicalPins[1], EndPin = second.PhysicalPins[0] };
        group.AddInternalPath(path);
        var fields = first.GetAllPins().Concat(second.GetAllPins())
            .SelectMany(pin => new[] { pin.IDInFlow, pin.IDOutFlow }).ToDictionary(id => id, _ => Complex.Zero);
        fields[path.StartPin.LogicalPin.IDOutFlow] = new(0.5, 0);
        fields[path.EndPin.LogicalPin.IDInFlow] = new(0.5, 0);
        return (group, path, fields);
    }
}
