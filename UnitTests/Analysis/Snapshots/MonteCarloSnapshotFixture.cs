using System.Numerics;
using CAP_Core.Analysis.MonteCarloAnalysis;
using CAP_Core.Analysis.MonteCarloAnalysis.FabricationVariance;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using CAP_Core.LightCalculation;
using Shouldly;

namespace UnitTests.Analysis.Snapshots;

internal sealed class MonteCarloSnapshotFixture
{
    internal SweepSnapshotFixture Circuit { get; } = new(coherent: true);
    internal MonteCarloConfiguration Configuration { get; } = new(4, 42);
    internal List<Component> Participants { get; }
    internal HashSet<Guid> PreferredOutputs { get; }
    internal ProcessTolerances Tolerances { get; } = new(10, 5);

    internal MonteCarloSnapshotFixture()
    {
        Participants = [Circuit.First, Circuit.Second];
        PreferredOutputs = [Circuit.Output];
        Circuit.First.Name = "directional_coupler";
        Circuit.First.NazcaFunctionName = "directional_coupler";
        Circuit.Second.Name = "mmi";
        Circuit.Second.NazcaFunctionName = "mmi";
        Circuit.First.WaveLengthToSMatrixMap.Clear();
        Circuit.Second.WaveLengthToSMatrixMap.Clear();
        SetAmplitude(Circuit.First, 1540, 0.55);
        SetAmplitude(Circuit.First, 1560, 0.85);
        SetAmplitude(Circuit.Second, 1540, 0.7);
        SetAmplitude(Circuit.Second, 1560, 0.8);
    }

    internal MonteCarloSpectrumSnapshot Capture() => MonteCarloSpectrumSnapshot.Capture(Configuration,
        Circuit.Configuration, Circuit.Grid, Circuit.Ports, Participants, Tolerances, PreferredOutputs);

    internal async Task<MonteCarloSpectrumResult> LegacyAsync()
    {
        var source = new FabricationVarianceSource(Participants, Tolerances);
        var sweeper = new WavelengthSweeper(new PerturbedSystemMatrixBuilder(new SystemMatrixBuilder(Circuit.Grid), source), Circuit.Ports);
        Guid? selected = null;
        async Task<double[]> Sample(CancellationToken token)
        {
            var result = await sweeper.RunSweepAsync(Circuit.Configuration, Circuit.Grid, token);
            var candidates = result.MonitoredPinIds.Where(PreferredOutputs.Contains).ToList();
            if (candidates.Count == 0) candidates = result.MonitoredPinIds.ToList();
            selected ??= candidates.OrderByDescending(pin => result.GetInsertionLossSeriesForPin(pin).Average()).First();
            return result.GetInsertionLossSeriesForPin(selected.Value);
        }
        var samples = await new MonteCarloRunner().RunAsync(Configuration, source, Sample).WaitAsync(TimeSpan.FromSeconds(10));
        source.CurrentDeviations.ShouldBeNull();
        return new(samples, selected!.Value);
    }

    internal static void SetAmplitude(Component component, int wavelength, double amplitude)
    {
        var pins = component.GetAllPins();
        var matrix = new SMatrix(pins.SelectMany(pin => new[] { pin.IDInFlow, pin.IDOutFlow }).ToList(), new());
        matrix.SetValues(new() { [(pins[0].IDInFlow, pins[1].IDOutFlow)] = new Complex(amplitude, 0) });
        component.WaveLengthToSMatrixMap[wavelength] = matrix;
    }

    internal static void AssertEquivalent(MonteCarloSpectrumResult actual, MonteCarloSpectrumResult expected)
    {
        actual.SelectedOutputPinId.ShouldBe(expected.SelectedOutputPinId);
        AssertCurve(actual.Samples.NominalCurve, expected.Samples.NominalCurve);
        actual.Samples.RunCurves.Count.ShouldBe(expected.Samples.RunCurves.Count);
        for (var run = 0; run < expected.Samples.RunCurves.Count; run++)
            AssertCurve(actual.Samples.RunCurves[run], expected.Samples.RunCurves[run]);
    }

    private static void AssertCurve(IReadOnlyList<double> actual, IReadOnlyList<double> expected)
    {
        actual.Count.ShouldBe(expected.Count);
        for (var point = 0; point < expected.Count; point++) actual[point].ShouldBe(expected[point], 1e-9);
    }
}
