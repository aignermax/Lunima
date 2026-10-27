using System.Collections.Immutable;
using System.Numerics;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.Snapshots;

namespace CAP_Core.Analysis;

/// <summary>Private circuit and slider trials; never writes into the component being inspected.</summary>
public sealed class ParameterSweepSnapshot
{
    private readonly OpticalCircuitSnapshot _circuit;
    private readonly ImmutableArray<(Guid Pin, Complex Field)> _sources;
    private readonly ImmutableArray<double> _values;
    private readonly Guid _slider;
    private readonly int _wavelength;

    private ParameterSweepSnapshot(OpticalCircuitSnapshot circuit,
        ImmutableArray<(Guid, Complex)> sources, ImmutableArray<double> values, Guid slider, int wavelength)
    {
        _circuit = circuit;
        _sources = sources;
        _values = values;
        _slider = slider;
        _wavelength = wavelength;
    }

    public static ParameterSweepSnapshot Capture(SweepConfiguration configuration,
        GridManager grid, IExternalPortManager ports) => new(
        OpticalCircuitSnapshot.Capture(grid),
        ports.GetUsedExternalInputs()
            .Where(input => input.Input.LaserType.WaveLengthInNm == configuration.WavelengthNm)
            .Reverse() // Legacy GridLightCalculator copies the filtered inputs into another ConcurrentBag.
            .Select(input =>
            (input.AttachedComponentPinId, input.Input.InFlowField)).ToImmutableArray(),
        configuration.GenerateSweepValues().ToImmutableArray(),
        configuration.Parameter.GetSlider().ID, configuration.WavelengthNm);

    public async Task<List<SweepDataPoint>> SolveAsync(CancellationToken cancellationToken,
        IProgress<int>? progress = null)
    {
        var components = _circuit.RebuildComponents(cancellationToken);
        var points = new List<SweepDataPoint>(_values.Length);
        for (var index = 0; index < _values.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var matrices in components)
                foreach (var matrix in matrices.Values)
                    if (matrix.SliderReference.ContainsKey(_slider))
                        matrix.SliderReference[_slider] = _values[index];
            progress?.Report(index + 1);
            var system = _circuit.BuildMatrix(components, _wavelength);
            var inputs = MathNet.Numerics.LinearAlgebra.Vector<Complex>.Build.Dense(system.SMat.RowCount);
            foreach (var source in _sources)
                if (system.PinReference.TryGetValue(source.Pin, out var offset)) inputs[offset] = source.Field;
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var fields = await system.CalcFieldAtPinsAfterStepsAsync(inputs, SMatrix.DefaultMaxIterations, stop)
                .ConfigureAwait(false) ?? new Dictionary<Guid, Complex>();
            cancellationToken.ThrowIfCancellationRequested();
            points.Add(new SweepDataPoint(_values[index], fields));
        }
        return points;
    }
}
