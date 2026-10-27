using CAP_Core;
using CAP_Core.Analysis.CircuitOptimization;
using CAP_Core.Components.Core;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Grid.FormulaReading;
using CAP_Core.LightCalculation;
using Shouldly;

namespace UnitTests.Analysis.Snapshots;

public sealed class CircuitOptimizationSnapshotTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeededOpticalSearch_MatchesLegacyBaselineRankingValuesAndEvaluationCount(bool coherent)
    {
        var (circuit, slider, settings) = Create(coherent);
        var expected = await new CircuitOptimizer(new GridLightCalculator(new SystemMatrixBuilder(circuit.Grid), circuit.Grid))
            .RunAsync(settings).WaitAsync(TimeSpan.FromSeconds(10));
        expected.TopVariants.ShouldNotBeEmpty("the fixture must exercise successful real optical trials");
        slider.Value.ShouldBe(0.2);

        var actual = await CircuitOptimizationSnapshot.Capture(settings, circuit.Grid)
            .SolveAsync(default).WaitAsync(TimeSpan.FromSeconds(10));

        AssertEquivalent(actual, expected);
        slider.Value.ShouldBe(0.2);
    }

    [Fact]
    public async Task HeldRealTrial_DoesNotMutateLiveSliderOrRestoreOverTheUsersNewValue()
    {
        var (circuit, slider, settings) = Create();
        var snapshot = CircuitOptimizationSnapshot.Capture(settings, circuit.Grid);
        using var stop = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new InlineProgress(value =>
        {
            if (value.EvaluationsDone != 2) return;
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Trial fixture was not released.");
        });
        int edits = 0;
        slider.PropertyChanged += (_, _) => Interlocked.Increment(ref edits);
        var running = Task.Run(() => snapshot.SolveAsync(stop.Token, progress));
        double valueDuringTrial = double.NaN;
        int trialEdits = -1;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            valueDuringTrial = slider.Value;
            trialEdits = Volatile.Read(ref edits);
            slider.Value = 0.91;
            stop.Cancel();
        }
        finally { release.Set(); await running.WaitAsync(TimeSpan.FromSeconds(10)); }

        valueDuringTrial.ShouldBe(0.2, "speculative values must belong to private evaluation state");
        trialEdits.ShouldBe(0, "speculative evaluations must not raise live slider notifications");
        slider.Value.ShouldBe(0.91, "cleanup cannot restore over an edit made while optimization was running");
    }

    [Fact]
    public async Task Capture_IsolatesLaterLiveFormulaMatrixSourceAndRouteChanges()
    {
        var (circuit, slider, settings) = Create(coherent: true);
        var expected = await new CircuitOptimizer(new GridLightCalculator(new SystemMatrixBuilder(circuit.Grid), circuit.Grid))
            .RunAsync(settings).WaitAsync(TimeSpan.FromSeconds(10));
        var snapshot = CircuitOptimizationSnapshot.Capture(settings, circuit.Grid);
        slider.Value = 0.91;
        circuit.First.WaveLengthToSMatrixMap.Clear();
        circuit.Source.InFlowPower = new System.Numerics.Complex(0.1, 0);
        circuit.Wire.PropagationLossDbPerCm = 1500;
        circuit.Wire.RestoreCachedPath(SweepSnapshotFixture.Path(900));

        var actual = await snapshot.SolveAsync(default).WaitAsync(TimeSpan.FromSeconds(10));

        AssertEquivalent(actual, expected);
        slider.Value.ShouldBe(0.91);
    }

    [Fact]
    public async Task DuplicateSourceOnSamePin_PreservesLegacyLastWinsAndCompleteRanking()
    {
        var (circuit, slider, settings) = Create(coherent: true);
        var optimizer = new CircuitOptimizer(new GridLightCalculator(new SystemMatrixBuilder(circuit.Grid), circuit.Grid));
        var original = await optimizer.RunAsync(settings).WaitAsync(TimeSpan.FromSeconds(10));
        original.BaselineScore.ShouldBeGreaterThan(0);
        circuit.Ports.AddLightSource(new CAP_Core.ExternalPorts.ExternalInput("last source",
            CAP_Core.ExternalPorts.LaserType.Red, 0, new System.Numerics.Complex(0.16, 0)), circuit.Input);
        var expected = await optimizer.RunAsync(settings).WaitAsync(TimeSpan.FromSeconds(10));
        expected.BaselineScore.ShouldBe(original.BaselineScore / 4, 1e-12,
            "the fixture must exercise last-wins power rather than summing duplicate inputs");
        expected.TopVariants.ShouldNotBeEmpty();
        var snapshot = CircuitOptimizationSnapshot.Capture(settings, circuit.Grid);
        circuit.Ports.Clear();

        var actual = await snapshot.SolveAsync(default).WaitAsync(TimeSpan.FromSeconds(10));

        AssertEquivalent(actual, expected);
        slider.Value.ShouldBe(0.2);
    }

    [Fact]
    public async Task NamedParametricSlider_RebuildsPrivateFormulaAndPreservesSeededRanking()
    {
        var (circuit, slider, settings) = Create(coherent: true);
        var pins = circuit.First.PhysicalPins.Select(pin => pin.LogicalPin).ToList();
        var matrix = CAP_Core.Components.Parametric.ParametricSMatrixFactory.Build(pins, [slider], new(
            [new CAP_Core.Components.Parametric.ParameterDefinition("coupling", 0.2, 0, 1, sliderNumber: 0)],
            [new CAP_Core.Components.Parametric.FormulaConnection(pins[0].Name, pins[1].Name,
                "coupling", "15 * coupling")]));
        circuit.First.WaveLengthToSMatrixMap = new() { [StandardWaveLengths.RedNM] = matrix };
        var expected = await new CircuitOptimizer(new GridLightCalculator(new SystemMatrixBuilder(circuit.Grid), circuit.Grid))
            .RunAsync(settings).WaitAsync(TimeSpan.FromSeconds(10));
        expected.TopVariants.ShouldNotBeEmpty("a named slider must change the real optical objective");
        expected.TopVariants[0].Score.ShouldBeGreaterThan(expected.BaselineScore);
        var snapshot = CircuitOptimizationSnapshot.Capture(settings, circuit.Grid);
        matrix.ParametricRebuild = (_, _) => throw new InvalidOperationException("live parametric factory escaped");
        foreach (var key in matrix.NonLinearConnections.Keys.ToArray())
            matrix.NonLinearConnections[key] = matrix.NonLinearConnections[key] with
                { CalcConnectionWeightAsync = _ => throw new InvalidOperationException("live formula escaped") };
        slider.Value = 0.91;

        var actual = await snapshot.SolveAsync(default).WaitAsync(TimeSpan.FromSeconds(10));

        AssertEquivalent(actual, expected);
        slider.Value.ShouldBe(0.91);
    }

    internal static (SweepSnapshotFixture Circuit, Slider Slider, OptimizationSettings Settings) Create(bool coherent = false)
    {
        var circuit = new SweepSnapshotFixture(coherent);
        var slider = new Slider(Guid.NewGuid(), 0, 0.2, 1, 0);
        circuit.First.AddSlider(0, slider);
        var from = circuit.First.PhysicalPins[0].LogicalPin.IDInFlow;
        var to = circuit.First.PhysicalPins[1].LogicalPin.IDOutFlow;
        foreach (var matrix in circuit.First.WaveLengthToSMatrixMap.Values.Distinct())
            matrix.NonLinearConnections[(from, to)] = MathExpressionReader.ConvertToDelegate("ToComplex(SLIDER0, 0)",
                new Dictionary<string, Guid>(), new Dictionary<string, Guid> { ["SLIDER0"] = slider.ID });
        var settings = new OptimizationSettings([new(circuit.First, 0, "Coupling")],
            new PinPowerObjective([circuit.Output], "output"), StandardWaveLengths.RedNM, 32, 42);
        return (circuit, slider, settings);
    }

    private static void AssertEquivalent(CircuitOptimizationRunResult actual, OptimizationResult expected)
    {
        actual.BaselineScore.ShouldBe(expected.BaselineScore, 1e-12);
        actual.EvaluationsUsed.ShouldBe(expected.EvaluationsUsed);
        actual.WasCancelled.ShouldBe(expected.WasCancelled);
        actual.TopVariants.Count.ShouldBe(expected.TopVariants.Count);
        for (int index = 0; index < expected.TopVariants.Count; index++)
        {
            actual.TopVariants[index].Score.ShouldBe(expected.TopVariants[index].Score, 1e-12);
            actual.TopVariants[index].ParameterValues.ShouldBe(expected.TopVariants[index].ParameterValues);
        }
    }

    private sealed class InlineProgress(Action<OptimizationProgress> report) : IProgress<OptimizationProgress>
    {
        public void Report(OptimizationProgress value) => report(value);
    }
}
