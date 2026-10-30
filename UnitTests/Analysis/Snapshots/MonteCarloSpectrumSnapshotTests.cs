using System.Numerics;
using CAP_Core.Analysis.MonteCarloAnalysis;
using CAP_Core.Components.Core;
using CAP_Core.Components.Parametric;
using Shouldly;

namespace UnitTests.Analysis.Snapshots;

public sealed class MonteCarloSpectrumSnapshotTests
{
    [Theory]
    [InlineData("matrices")]
    [InlineData("classification")]
    [InlineData("participant-order")]
    [InlineData("sources")]
    [InlineData("route")]
    [InlineData("output-designation")]
    public async Task WorkerHeldAfterCapture_UsesOriginalSeededPhysics(string mutation)
    {
        var fixture = new MonteCarloSnapshotFixture();
        var captured = fixture.Capture();
        var expected = await fixture.LegacyAsync();
        var barrier = new LogicSnapshotFixture.WorkerBarrier();
        var running = Task.Run(async () =>
        {
            await barrier.WaitAsync();
            return await captured.SolveAsync();
        });

        var actual = await barrier.MutateThenFinishAsync(running, () => Mutate(fixture, mutation));

        if (mutation == "route")
        {
            var changed = await fixture.LegacyAsync();
            changed.Samples.NominalCurve[0].ShouldBeLessThan(expected.Samples.NominalCurve[0] - 1,
                "recalculated route loss must materially affect the live oracle");
        }

        MonteCarloSnapshotFixture.AssertEquivalent(actual, expected);
    }

    [Fact]
    public async Task CapturedParametricDefinitions_DoNotRetainLiveFactoryOrSliderState()
    {
        var fixture = new MonteCarloSnapshotFixture();
        var component = fixture.Circuit.First;
        var pins = component.PhysicalPins.Select(pin => pin.LogicalPin).ToList();
        var slider = new Slider(Guid.NewGuid(), 0, 0.6, 1, 0);
        component.AddSlider(0, slider);
        var matrix = ParametricSMatrixFactory.Build(pins, [slider], new(
            [new ParameterDefinition("gain", 0.6, 0, 1, sliderNumber: 0)],
            [new FormulaConnection(pins[0].Name, pins[1].Name, "gain", "0")]));
        component.WaveLengthToSMatrixMap = new() { [1550] = matrix };
        var captured = fixture.Capture();
        var expected = await fixture.LegacyAsync();
        matrix.SliderReference[slider.ID] = 0.2;
        slider.Value = 0.2;
        matrix.ParametricRebuild = (_, _) => throw new InvalidOperationException("live parametric factory escaped capture");

        var actual = await captured.SolveAsync().WaitAsync(TimeSpan.FromSeconds(10));

        MonteCarloSnapshotFixture.AssertEquivalent(actual, expected);
    }

    [Fact]
    public async Task UnchangedPhysics_MatchesLegacyNominalAndEverySeededCurve()
    {
        var fixture = new MonteCarloSnapshotFixture();
        var captured = fixture.Capture();
        var expected = await fixture.LegacyAsync();

        var first = await captured.SolveAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var second = await captured.SolveAsync().WaitAsync(TimeSpan.FromSeconds(10));

        MonteCarloSnapshotFixture.AssertEquivalent(first, expected);
        MonteCarloSnapshotFixture.AssertEquivalent(second, expected);
        first.Samples.RunCurves.ShouldContain(curve => !curve.SequenceEqual(first.Samples.NominalCurve));
    }

    [Fact]
    public async Task PreCancelledSolve_IsRejectedWithoutReportingACompletedSample()
    {
        var captured = new MonteCarloSnapshotFixture().Capture();
        var reports = 0;
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => captured.SolveAsync(
            new InlineProgress(_ => reports++), stop.Token));

        reports.ShouldBe(0);
    }

    private static void Mutate(MonteCarloSnapshotFixture fixture, string mutation)
    {
        switch (mutation)
        {
            case "matrices":
                foreach (var component in fixture.Participants)
                    foreach (var wavelength in component.WaveLengthToSMatrixMap.Keys.ToArray())
                        MonteCarloSnapshotFixture.SetAmplitude(component, wavelength, 0.1);
                break;
            case "classification":
                foreach (var component in fixture.Participants)
                {
                    component.Name = "bend";
                    component.NazcaFunctionName = "bend";
                    component.GdsFactoryFunction = "bend";
                }
                break;
            case "participant-order": fixture.Participants.Reverse(); break;
            case "sources": fixture.Circuit.Source.InFlowPower = Complex.Zero; break;
            case "route":
                fixture.Circuit.Wire.PropagationLossDbPerCm = 2000;
                fixture.Circuit.Wire.RestoreCachedPath(SweepSnapshotFixture.Path(37));
                break;
            case "output-designation":
                fixture.PreferredOutputs.Clear();
                fixture.PreferredOutputs.Add(fixture.Circuit.Input);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }

    private sealed class InlineProgress(Action<MonteCarloProgress> report) : IProgress<MonteCarloProgress>
    {
        public void Report(MonteCarloProgress value) => report(value);
    }
}
