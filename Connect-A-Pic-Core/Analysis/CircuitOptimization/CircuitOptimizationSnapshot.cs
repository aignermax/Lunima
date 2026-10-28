using System.Collections.Immutable;
using System.Numerics;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.Snapshots;

namespace CAP_Core.Analysis.CircuitOptimization;

/// <summary>Detached optical definitions, source fields and scalar search settings for private trials.</summary>
public sealed class CircuitOptimizationSnapshot
{
    private readonly OpticalCircuitSnapshot _circuit;
    private readonly ImmutableArray<(Guid Pin, Complex Field)> _sources;
    private readonly ImmutableArray<Guid> _sliders;
    private readonly ImmutableArray<OptimizationSearch.Range> _ranges;
    private readonly ImmutableArray<double> _initial;
    private readonly PinPowerObjective _objective;
    private readonly int _wavelength, _budget, _seed, _topN;

    private CircuitOptimizationSnapshot(OptimizationSettings settings, GridManager grid)
    {
        _objective = settings.Objective is PinPowerObjective objective ? objective.CopyForSnapshot()
            : throw new NotSupportedException("Circuit snapshot optimization requires a pin-power objective.");
        _circuit = OpticalCircuitSnapshot.Capture(grid);
        _sources = grid.ExternalPortManager.GetUsedExternalInputs()
            .Where(input => input.Input.LaserType.WaveLengthInNm == settings.WavelengthNm)
            .Reverse() // Match the legacy calculator's second ConcurrentBag traversal.
            .Select(input => (input.AttachedComponentPinId, input.Input.InFlowField)).ToImmutableArray();
        _sliders = settings.Parameters.Select(value => value.GetSlider().ID).ToImmutableArray();
        _ranges = settings.Parameters.Select(value => new OptimizationSearch.Range(value.MinValue, value.MaxValue)).ToImmutableArray();
        _initial = settings.Parameters.Select(value => value.GetSlider().Value).ToImmutableArray();
        _wavelength = settings.WavelengthNm;
        _budget = settings.EvaluationBudget;
        _seed = settings.Seed;
        _topN = settings.TopN;
    }

    public static CircuitOptimizationSnapshot Capture(OptimizationSettings settings, GridManager grid) => new(settings, grid);

    public async Task<CircuitOptimizationRunResult> SolveAsync(CancellationToken cancellationToken,
        IProgress<OptimizationProgress>? progress = null)
    {
        var matrices = _circuit.RebuildComponents(cancellationToken);
        var result = await OptimizationSearch.RunAsync(_ranges, _initial.ToArray(), _budget, _seed, _topN,
            EvaluateAsync, cancellationToken, progress).ConfigureAwait(false);
        return new(result.BaselineScore, result.TopVariants, result.EvaluationsUsed, result.WasCancelled);

        async Task<double> EvaluateAsync(double[] values, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            foreach (var component in matrices)
                foreach (var matrix in component.Values)
                    for (int index = 0; index < _sliders.Length; index++)
                        if (matrix.SliderReference.ContainsKey(_sliders[index]))
                            matrix.SliderReference[_sliders[index]] = values[index];
            var system = _circuit.BuildMatrix(matrices, _wavelength);
            var input = MathNet.Numerics.LinearAlgebra.Vector<Complex>.Build.Dense(system.SMat.RowCount);
            foreach (var source in _sources)
                if (system.PinReference.TryGetValue(source.Pin, out var offset)) input[offset] = source.Field;
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            var fields = await system.CalcFieldAtPinsAfterStepsAsync(input, SMatrix.DefaultMaxIterations, stop)
                .ConfigureAwait(false) ?? new Dictionary<Guid, Complex>();
            return _objective.Score(fields.ToDictionary(pair => pair.Key, pair => pair.Value.Magnitude * pair.Value.Magnitude));
        }
    }
}
