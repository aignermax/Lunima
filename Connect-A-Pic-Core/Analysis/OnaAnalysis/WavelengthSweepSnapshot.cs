using System.Collections.Immutable;
using System.Numerics;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.Snapshots;

namespace CAP_Core.Analysis.OnaAnalysis;

/// <summary>Detached sweep input: capture costs depend on the graph and definitions, not the sample count.</summary>
public sealed class WavelengthSweepSnapshot
{
    private readonly WavelengthSweepConfiguration _configuration;
    private readonly OpticalCircuitSnapshot _circuit;
    private readonly ImmutableArray<(Guid FlowId, Complex Field)> _sources;
    private readonly double _inputPower;
    private readonly ImmutableArray<string> _warnings;

    private WavelengthSweepSnapshot(WavelengthSweepConfiguration configuration, OpticalCircuitSnapshot circuit,
        ImmutableArray<(Guid, Complex)> sources, double inputPower, ImmutableArray<string> warnings)
    {
        _configuration = configuration;
        _circuit = circuit;
        _sources = sources;
        _inputPower = inputPower;
        _warnings = warnings;
    }

    /// <summary>Reads caller-owned state synchronously before scheduling worker execution.</summary>
    public static WavelengthSweepSnapshot Capture(WavelengthSweepConfiguration configuration,
        GridManager grid, IExternalPortManager ports)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(ports);
        var settings = new WavelengthSweepConfiguration(configuration.StartNm, configuration.EndNm, configuration.StepCount);
        var warnings = WavelengthSweeper.CollectWavelengthCoverageWarnings(grid, settings).ToImmutableArray();
        var sources = ports.GetUsedExternalInputs()
            .Select(input => (input.AttachedComponentPinId, input.Input.InFlowField)).ToImmutableArray();
        var inputPower = ports.GetAllExternalInputs().Sum(input => input.InFlowPower.Magnitude);
        return new(settings, OpticalCircuitSnapshot.Capture(grid), sources,
            inputPower > 0 ? inputPower : 1.0, warnings);
    }

    /// <summary>Solves only captured values; simultaneous solves rebuild separate mutable matrices.</summary>
    public async Task<WavelengthSweepResult> SolveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var components = _circuit.RebuildComponents(cancellationToken);
        var wavelengths = _configuration.GenerateWavelengthValues();
        var points = new List<WavelengthDataPoint>(wavelengths.Length);
        List<Guid>? monitored = null;
        foreach (var wavelength in wavelengths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matrix = _circuit.BuildMatrix(components, wavelength);
            var inputs = MathNet.Numerics.LinearAlgebra.Vector<Complex>.Build.Dense(matrix.SMat.RowCount);
            foreach (var source in _sources)
                if (matrix.PinReference.TryGetValue(source.FlowId, out var index)) inputs[index] = source.Field;
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var fields = await matrix.CalcFieldAtPinsAfterStepsAsync(inputs, SMatrix.DefaultMaxIterations, stop)
                .ConfigureAwait(false) ?? new Dictionary<Guid, Complex>();
            // The low-level solver can return empty on cancellation; that is not a completed sweep.
            cancellationToken.ThrowIfCancellationRequested();
            monitored ??= fields.Keys.ToList();
            points.Add(new WavelengthDataPoint(wavelength, fields, _inputPower));
        }
        return new(_configuration, points, monitored ?? [], _warnings.ToList());
    }
}
