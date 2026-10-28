using System.Numerics;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Analysis.EyeDiagram;
using CAP_Core.Analysis.EyeDiagram;
using CAP_Core.Analysis.MonteCarloAnalysis;
using CAP_Core.Analysis.MonteCarloAnalysis.FabricationVariance;
using CAP_Core.Components.Core;
using CAP_Core.Components.Parametric;
using CAP_Core.ExternalPorts.LaserSpectrum;
using Shouldly;

namespace UnitTests.Analysis.Snapshots;

public sealed class MonteCarloEyeCaptureTests
{
    [AvaloniaTheory]
    [InlineData("matrix")]
    [InlineData("source-power")]
    [InlineData("source-disabled")]
    [InlineData("route-loss")]
    [InlineData("output-removed")]
    public async Task HeldEyeSample_UsesTheCircuitAndExcitationCapturedBeforeWorkerStart(string change)
    {
        await using var fixture = new MonteCarloEyeFixture(withAttenuator: change == "output-removed");
        var captured = fixture.Capture();
        var original = await MonteCarloLegacyEyeOracle.ObserveAsync(fixture.Canvas, fixture.Resolution)
            .WaitAsync(TimeSpan.FromSeconds(15));
        var expected = new[] { original.EyeHeight };
        expected[0].ShouldBeGreaterThan(0.01, "the fixture must produce a measurable open eye");
        if (change == "output-removed")
        {
            original.TracePeaks.ContainsKey(fixture.SourceForwardFlow).ShouldBeTrue();
            original.TracePeaks[fixture.SourceForwardFlow].ShouldBeGreaterThan(0.9);
            original.CandidateIds.ShouldNotContain(fixture.SourceForwardFlow);
            original.CandidateIds.ShouldContain(fixture.Circuit.Output);
            var outputPeak = original.CandidateIds.Max(id => original.TracePeaks[id]);
            outputPeak.ShouldBeGreaterThan(0.4, "moderate attenuation must retain a measurable output");
            outputPeak.ShouldBeLessThan(0.7,
                "the separate attenuator must distinguish the off-output candidates from the upstream source trace");
        }
        var barrier = new LogicSnapshotFixture.WorkerBarrier();
        var running = Task.Run(async () =>
        {
            await barrier.WaitAsync();
            return await captured.SampleAsync(default);
        });

        var actual = await barrier.MutateThenFinishAsync(running, () => Mutate(fixture, change));

        if (change is "route-loss" or "output-removed")
        {
            var changed = await MonteCarloLegacyEyeOracle.ObserveAsync(fixture.Canvas, fixture.Resolution)
                .WaitAsync(TimeSpan.FromSeconds(15));
            if (change == "output-removed")
            {
                changed.CandidateIds.ShouldContain(fixture.SourceForwardFlow);
                changed.SelectedPeak.ShouldBeGreaterThan(0.9,
                    "without the off-output designation fallback must select a stronger upstream trace");
            }
            Math.Abs(changed.EyeHeight - expected[0]).ShouldBeGreaterThan(0.01,
                "the live legacy metric must detect the changed route or output policy");
        }

        actual.Single().ShouldBe(expected.Single(), 1e-10);
    }

    [AvaloniaFact]
    public async Task CapturedParametricEye_DoesNotReadLaterSliderValues()
    {
        await using var fixture = new MonteCarloEyeFixture();
        var component = fixture.Circuit.First;
        var pins = component.PhysicalPins.Select(pin => pin.LogicalPin).ToList();
        var slider = new Slider(Guid.NewGuid(), 0, 0.8, 1, 0);
        component.AddSlider(0, slider);
        var matrix = ParametricSMatrixFactory.Build(pins, [slider], new(
            [new ParameterDefinition("gain", 0.8, 0, 1, sliderNumber: 0)],
            [new FormulaConnection(pins[0].Name, pins[1].Name, "gain", "0")]));
        component.WaveLengthToSMatrixMap = new() { [1550] = matrix };
        var captured = fixture.Capture();
        var expected = await MonteCarloLegacyEyeOracle.SampleAsync(fixture.Canvas, fixture.Resolution)
            .WaitAsync(TimeSpan.FromSeconds(15));
        slider.Value = 0.2;
        matrix.SliderReference[slider.ID] = 0.2;
        var changed = await MonteCarloLegacyEyeOracle.SampleAsync(fixture.Canvas, fixture.Resolution)
            .WaitAsync(TimeSpan.FromSeconds(15));
        Math.Abs(changed[0] - expected[0]).ShouldBeGreaterThan(0.01);

        var actual = await captured.SampleAsync(default).WaitAsync(TimeSpan.FromSeconds(15));

        actual.Single().ShouldBe(expected.Single(), 1e-10);
    }

    [AvaloniaFact]
    public async Task CapturedIndependentLaserPhases_PreserveTheOriginalInterferingEye()
    {
        await using var fixture = new MonteCarloEyeFixture();
        fixture.Output.LaserConfig!.IsEnabled = true;
        fixture.Circuit.Wire.RestoreCachedPath(SweepSnapshotFixture.Path(0));
        var captured = fixture.Capture();
        var expected = await MonteCarloLegacyEyeOracle.SampleAsync(fixture.Canvas, fixture.Resolution)
            .WaitAsync(TimeSpan.FromSeconds(15));
        expected[0].ShouldBeGreaterThan(1, "two coherent source fields must interfere at the selected output");
        fixture.Input.LaserConfig!.LineShape = LaserLineShape.Lorentzian;
        fixture.Input.LaserConfig.LinewidthFwhmNm = 1;
        var changed = await MonteCarloLegacyEyeOracle.SampleAsync(fixture.Canvas, fixture.Resolution)
            .WaitAsync(TimeSpan.FromSeconds(15));
        Math.Abs(changed[0] - expected[0]).ShouldBeGreaterThan(0.01,
            "independent source phase noise must change this interfering metric, unlike a single-path intensity trace");

        var actual = await captured.SampleAsync(default).WaitAsync(TimeSpan.FromSeconds(15));

        actual.Single().ShouldBe(expected.Single(), 1e-10);
    }

    [AvaloniaTheory]
    [InlineData(-145)]
    [InlineData(-90)]
    public async Task UnchangedEye_PreservesLegacyNoiselessThresholdAndDefaultReceiverRin(double configuredRin)
    {
        await using var fixture = new MonteCarloEyeFixture();
        fixture.Input.LaserConfig!.RinDbPerHz = configuredRin;
        var captured = fixture.Capture();
        var expected = await MonteCarloLegacyEyeOracle.SampleAsync(fixture.Canvas, fixture.Resolution)
            .WaitAsync(TimeSpan.FromSeconds(15));

        var actual = await captured.SampleAsync(default).WaitAsync(TimeSpan.FromSeconds(15));

        actual.Single().ShouldBe(expected.Single(), 1e-10);
        if (configuredRin == -90)
        {
            var (circuit, excitation) = TransientCircuitFactory.Capture(fixture.Canvas);
            var otherPolicy = new EyeAnalysisRequest(circuit, excitation, 25e9, PrbsOrder.Prbs7, 0.5,
                null, false, new(EyeTraceSelector.NoSignalAtDesignatedOutputError,
                    EyeTraceSelector.BelowNoiseFloorError, EyeTraceSelector.MultipleOutputsWarning));
            var eyePanelResult = await Task.Run(() => otherPolicy.Solve()).WaitAsync(TimeSpan.FromSeconds(15));
            eyePanelResult.Metrics.ShouldNotBeNull();
            Math.Abs(eyePanelResult.Metrics.EyeHeight - expected[0]).ShouldBeGreaterThan(0.01,
                "this control must detect substituting the eye panel's configured-RIN numerical policy");
        }
    }

    [AvaloniaFact]
    public async Task SeededFabricationEye_UsesTheSameNominalAndSequentialSampleMetrics()
    {
        await using var fixture = new MonteCarloEyeFixture();
        var configuration = new MonteCarloConfiguration(2, 818);
        var source = fixture.CreateVariance();
        var captured = fixture.Capture(inner => new PerturbedSystemMatrixBuilder(inner, source));
        var oracleSource = fixture.CreateVariance();
        var expected = await new MonteCarloRunner().RunAsync(configuration, oracleSource,
            _ => MonteCarloLegacyEyeOracle.SampleAsync(fixture.Canvas, fixture.Resolution,
                inner => new PerturbedSystemMatrixBuilder(inner, oracleSource))).WaitAsync(TimeSpan.FromSeconds(20));

        var actual = await new MonteCarloRunner().RunAsync(configuration, source, captured.SampleAsync)
            .WaitAsync(TimeSpan.FromSeconds(20));

        actual.NominalCurve[0].ShouldBe(expected.NominalCurve[0], 1e-10);
        for (var run = 0; run < configuration.RunCount; run++)
            actual.RunCurves[run][0].ShouldBe(expected.RunCurves[run][0], 1e-10);
        actual.RunCurves.ShouldContain(curve => Math.Abs(curve[0] - actual.NominalCurve[0]) > 1e-6);
        source.CurrentDeviations.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task CompleteSeededEyeCapture_OwnsTransientAndFabricationInputsTogether()
    {
        await using var fixture = new MonteCarloEyeFixture();
        var configuration = new MonteCarloConfiguration(2, 818);
        var captured = fixture.Capture().Capture(configuration, fixture.Participants,
            new CAP_Core.Components.Process.ProcessTolerances(3, 1));
        var oracleSource = fixture.CreateVariance();
        var expected = await new MonteCarloRunner().RunAsync(configuration, oracleSource,
            _ => MonteCarloLegacyEyeOracle.SampleAsync(fixture.Canvas, fixture.Resolution,
                inner => new PerturbedSystemMatrixBuilder(inner, oracleSource))).WaitAsync(TimeSpan.FromSeconds(20));
        fixture.Circuit.First.Name = fixture.Circuit.First.NazcaFunctionName = "bend";
        fixture.Circuit.First.WaveLengthToSMatrixMap.Clear();
        fixture.Input.LaserConfig!.IsEnabled = false;
        fixture.Circuit.Wire.PropagationLossDbPerCm = 1000;
        fixture.Circuit.Wire.RestoreCachedPath(SweepSnapshotFixture.Path(100));

        var actual = await new MonteCarloEyeRunner().RunAsync(captured, null, default)
            .WaitAsync(TimeSpan.FromSeconds(20));

        actual.NominalCurve[0].ShouldBe(expected.NominalCurve[0], 1e-10);
        for (var run = 0; run < configuration.RunCount; run++)
            actual.RunCurves[run][0].ShouldBe(expected.RunCurves[run][0], 1e-10);
    }

    private static void Mutate(MonteCarloEyeFixture fixture, string change)
    {
        switch (change)
        {
            case "matrix":
                foreach (var matrix in fixture.Circuit.First.WaveLengthToSMatrixMap.Values.Distinct())
                    matrix.SetValues(matrix.GetNonNullValues().ToDictionary(pair => pair.Key, pair => pair.Value * 0.2), reset: true);
                break;
            case "source-power": fixture.Input.LaserConfig!.InputPower = 0.1; break;
            case "source-disabled": fixture.Input.LaserConfig!.IsEnabled = false; break;
            case "route-loss":
                fixture.Circuit.Wire.PropagationLossDbPerCm = 1000;
                fixture.Circuit.Wire.RestoreCachedPath(SweepSnapshotFixture.Path(100));
                break;
            case "output-removed": fixture.Canvas.Components.Remove(fixture.Output); break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
    }
}
