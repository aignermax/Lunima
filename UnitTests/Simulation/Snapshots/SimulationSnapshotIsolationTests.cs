using System.Numerics;
using CAP_Core.Components.Core;
using CAP_Core.Components.Parametric;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.Snapshots;
using Shouldly;

namespace UnitTests.Simulation.Snapshots;

public sealed class SimulationSnapshotIsolationTests
{
    [Fact]
    public async Task CanceledRun_DoesNotPublishACompletedEmptyResult()
    {
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var snapshot = SimulationSnapshotCapture.Capture([component], [],
            [new(component.PhysicalPins[0].LogicalPin.IDInFlow, 1550, Complex.One)], false);
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => SimulationSnapshotSolver.RunAsync(snapshot, stop.Token));
    }

    [Fact]
    public async Task NumericMatrixAndSourceChangesAfterCapture_DoNotChangeWorkerInputOrFlowIds()
    {
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var input = component.PhysicalPins[0].LogicalPin.IDInFlow;
        var output = component.PhysicalPins[1].LogicalPin.IDOutFlow;
        var sources = new List<SimulationSourceInput> { new(input, 1550, Complex.One) };
        var snapshot = SimulationSnapshotCapture.Capture([component], [], sources, false);

        sources[0] = new(input, 1550, new Complex(9, 0));
        foreach (var matrix in component.WaveLengthToSMatrixMap.Values)
            matrix.SetValues(new(), reset: true);
        component.WaveLengthToSMatrixMap.Clear();
        component.PhysicalPins.Clear();

        var result = await SimulationSnapshotSolver.RunAsync(snapshot).WaitAsync(TimeSpan.FromSeconds(5));
        snapshot.ComponentIds.ShouldContain(component.Id);
        result.Fields[input].ShouldBe(Complex.One);
        result.Fields[output].ShouldBe(Complex.One);
        component.WaveLengthToSMatrixMap.ShouldBeEmpty();
    }

    [Fact]
    public async Task ParametricSnapshots_RebuildPrivateEvaluationStateForConcurrentRuns()
    {
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var pins = component.PhysicalPins.Select(p => p.LogicalPin).ToList();
        var slider = new Slider(Guid.NewGuid(), 0, 0.25, 1, 0);
        component.AddSlider(0, slider);
        var definition = new ParametricSMatrixSnapshot(
            [new ParameterDefinition("gain", 0.25, 0, 1, sliderNumber: 0)],
            [new FormulaConnection(pins[0].Name, pins[1].Name, "gain", "0")]);
        var matrix = ParametricSMatrixFactory.Build(pins, [slider], definition);
        component.WaveLengthToSMatrixMap = new() { [1550] = matrix };
        SimulationSourceInput[] sources = [new(pins[0].IDInFlow, 1550, Complex.One)];
        var first = SimulationSnapshotCapture.Capture([component], [], sources, false);
        slider.Value = 0.75;
        matrix.SliderReference[slider.ID] = 0.75;
        var second = SimulationSnapshotCapture.Capture([component], [], sources, false);

        // Poison every old delegate/factory: neither worker may invoke the live evaluation closure.
        matrix.ParametricRebuild = (_, _) => throw new InvalidOperationException("live factory escaped capture");
        foreach (var key in matrix.NonLinearConnections.Keys.ToArray())
            matrix.NonLinearConnections[key] = matrix.NonLinearConnections[key] with
                { CalcConnectionWeightAsync = _ => throw new InvalidOperationException("live formula escaped capture") };
        slider.Value = 0.99;
        matrix.SliderReference[slider.ID] = 0.99;

        var results = await Task.WhenAll(Task.Run(() => SimulationSnapshotSolver.RunAsync(first)),
            Task.Run(() => SimulationSnapshotSolver.RunAsync(second))).WaitAsync(TimeSpan.FromSeconds(5));
        results[0].Fields[pins[1].IDOutFlow].Real.ShouldBe(0.25, 1e-10);
        results[1].Fields[pins[1].IDOutFlow].Real.ShouldBe(0.75, 1e-10);
        matrix.SliderReference[slider.ID].ShouldBe(0.99);
    }

    [Fact]
    public async Task NamedParametricPorts_WithDuplicateLegacyPinNumbers_KeepFactorySemantics()
    {
        var original = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var pins = original.PhysicalPins.Select(physical => physical.LogicalPin).Select(pin =>
            new Pin(pin.Name, 0, pin.MatterType, pin.Side, pin.IDInFlow, pin.IDOutFlow)).ToList();
        var definition = new ParametricSMatrixSnapshot([], [new FormulaConnection(pins[0].Name, pins[1].Name, "0.5", "0")]);
        var matrix = ParametricSMatrixFactory.Build(pins, [], definition);
        var physicalPins = pins.Select(pin => new PhysicalPin { Name = pin.Name, LogicalPin = pin }).ToList();
        var component = new Component(new() { [1550] = matrix }, [], "fixture", "",
            new Part[0, 0], 0, "duplicate-numbers", DiscreteRotation.R0, physicalPins);
        var snapshot = SimulationSnapshotCapture.Capture([component], [],
            [new(pins[0].IDInFlow, 1550, Complex.One)], false);

        var result = await SimulationSnapshotSolver.RunAsync(snapshot).WaitAsync(TimeSpan.FromSeconds(5));
        result.Fields[pins[1].IDOutFlow].Real.ShouldBe(0.5, 1e-10);
    }

    [Fact]
    public async Task SpectralSources_KeepIncoherentPowerSumAndCapturedSourceValues()
    {
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var input = component.PhysicalPins[0].LogicalPin.IDInFlow;
        var output = component.PhysicalPins[1].LogicalPin.IDOutFlow;
        SimulationSourceInput[] sources = [new(input, 1550, new Complex(0.6, 0)), new(input, 1310, new Complex(0.8, 0))];
        var snapshot = SimulationSnapshotCapture.Capture([component], [], sources, false, combineIncoherently: true);
        sources[0] = new(input, 1550, Complex.Zero);
        sources[1] = new(input, 1310, Complex.Zero);

        var result = await SimulationSnapshotSolver.RunAsync(snapshot).WaitAsync(TimeSpan.FromSeconds(5));
        result.Fields[output].Magnitude.ShouldBe(1, 1e-10);
    }
}
