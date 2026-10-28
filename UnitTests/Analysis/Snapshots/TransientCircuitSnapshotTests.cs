using System.Numerics;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.TimeDomainSimulation;
using Shouldly;

namespace UnitTests.Analysis.Snapshots;

public sealed class TransientCircuitSnapshotTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnchangedCircuit_MatchesLegacyImpulseResponsesAndEveryTrace(bool coherent)
    {
        var fixture = new SweepSnapshotFixture(coherent);
        var expected = await RunAsync(new TimeDomainSimulator(new SystemMatrixBuilder(fixture.Grid)), fixture.Input);
        expected.PinTraces[fixture.Output].Max().ShouldBeGreaterThan(0);

        var captured = TransientCircuitSnapshot.Capture(fixture.Grid);
        AssertEquivalent(await RunAsync(captured.CreateSimulator(), fixture.Input), expected);
    }

    [Fact]
    public async Task CapturedNumericMatrices_AreIndependentOfSubsequentLiveMatrixEdits()
    {
        var fixture = new SweepSnapshotFixture();
        var expected = await RunAsync(new TimeDomainSimulator(new SystemMatrixBuilder(fixture.Grid)), fixture.Input);
        expected.PinTraces[fixture.Output].Max().ShouldBeGreaterThan(0);
        var captured = TransientCircuitSnapshot.Capture(fixture.Grid);

        foreach (var matrix in fixture.First.WaveLengthToSMatrixMap.Values)
            matrix.SetValues(new(), reset: true);

        AssertEquivalent(await RunAsync(captured.CreateSimulator(), fixture.Input), expected);
    }

    [Fact]
    public async Task CapturedRoutePhysics_AreIndependentOfSubsequentLiveLossAndPathEdits()
    {
        var fixture = new SweepSnapshotFixture(coherent: true);
        var expected = await RunAsync(new TimeDomainSimulator(new SystemMatrixBuilder(fixture.Grid)), fixture.Input);
        expected.PinTraces[fixture.Output].Max().ShouldBeGreaterThan(0);
        var captured = TransientCircuitSnapshot.Capture(fixture.Grid);

        fixture.Wire.PropagationLossDbPerCm = 1500;
        fixture.Wire.RestoreCachedPath(SweepSnapshotFixture.Path(300));
        fixture.Grid.WaveguideConnections.EnableCoherentPropagationPhase = false;

        AssertEquivalent(await RunAsync(captured.CreateSimulator(), fixture.Input), expected);
    }

    [Fact]
    public async Task CapturedClosureDiagnostics_KeepOriginalOwnerNamesAfterLiveRename()
    {
        var fixture = new SweepSnapshotFixture();
        var firstOutput = fixture.First.PhysicalPins[1].LogicalPin.IDOutFlow;
        foreach (var matrix in fixture.First.WaveLengthToSMatrixMap.Values)
            matrix.SetValues(new Dictionary<(Guid, Guid), Complex>
                { [(fixture.Input, firstOutput)] = new Complex(2, 0) }, reset: true);
        var names = fixture.First.PhysicalPins
            .SelectMany(pin => new[] { pin.LogicalPin.IDInFlow, pin.LogicalPin.IDOutFlow })
            .Distinct().ToDictionary(id => id, _ => "captured component");
        var context = new TransitiveClosureContext { PinOwnerNames = names };
        var expected = await Should.ThrowAsync<NonConvergentCircuitException>(() =>
            RunAsync(new TimeDomainSimulator(new SystemMatrixBuilder(fixture.Grid), context), fixture.Input));
        expected.Kind.ShouldBe(NonConvergentCircuitKind.NonPassiveComponent);
        expected.ComponentName.ShouldBe("captured component");
        var captured = TransientCircuitSnapshot.Capture(fixture.Grid, context);

        foreach (var id in names.Keys.ToArray()) names[id] = "edited component";

        var actual = await Should.ThrowAsync<NonConvergentCircuitException>(() =>
            RunAsync(captured.CreateSimulator(), fixture.Input));
        actual.Kind.ShouldBe(expected.Kind);
        actual.ComponentName.ShouldBe(expected.ComponentName);
        actual.WavelengthNm.ShouldBe(expected.WavelengthNm);
        actual.ExcessPercent.ShouldBe(expected.ExcessPercent);
    }

    private static Task<TimeDomainResult> RunAsync(TimeDomainSimulator simulator, Guid input)
    {
        var grid = TimeSignalDefinition.FromWavelengthSweep(1550, 100, 64);
        var signals = new Dictionary<Guid, double[]>
        {
            [input] = grid.CreateGaussianPulse(20 * grid.TimeStepSeconds, 3 * grid.TimeStepSeconds, 0.8)
        };
        return Task.Run(() => simulator.Run(signals, grid, 1550, 100, 64))
            .WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static void AssertEquivalent(TimeDomainResult actual, TimeDomainResult expected)
    {
        actual.TimeAxis.ShouldBe(expected.TimeAxis);
        actual.PinTraces.Keys.OrderBy(id => id).ShouldBe(expected.PinTraces.Keys.OrderBy(id => id));
        foreach (var (pin, trace) in expected.PinTraces)
        {
            actual.PinTraces[pin].Length.ShouldBe(trace.Length);
            for (var sample = 0; sample < trace.Length; sample++)
                actual.PinTraces[pin][sample].ShouldBe(trace[sample], 1e-12);
        }
    }
}
