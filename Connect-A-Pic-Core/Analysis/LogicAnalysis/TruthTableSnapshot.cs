using System.Collections.Immutable;
using System.Numerics;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.Snapshots;
using CAP_Core.Routing;

namespace CAP_Core.Analysis.LogicAnalysis;

/// <summary>Immutable gate interface and optical definitions; no live component, pin, matrix or delegate is retained.</summary>
public sealed class TruthTableSnapshot
{
    private readonly record struct PinValue(string Name, Guid In, Guid Out);
    private readonly string _name;
    private readonly ImmutableArray<PinValue> _inputs, _outputs, _biases;
    private readonly double _threshold;
    private readonly int _wavelength;
    private readonly OpticalCircuitSnapshot _circuit;

    private TruthTableSnapshot(string name, ImmutableArray<PinValue> inputs,
        ImmutableArray<PinValue> outputs, ImmutableArray<PinValue> biases, double threshold,
        int wavelength, OpticalCircuitSnapshot circuit)
    {
        _name = name;
        _inputs = inputs;
        _outputs = outputs;
        _biases = biases;
        _threshold = threshold;
        _wavelength = wavelength;
        _circuit = circuit;
    }

    /// <summary>Copies compact physics and pin values synchronously; it does not compute a group closure or any truth-table row.</summary>
    public static TruthTableSnapshot Capture(ComponentGroup group, IReadOnlyList<string> inputs,
        IReadOnlyList<string> outputs, IReadOnlyList<string> biases, double threshold, int wavelength)
    {
        ArgumentNullException.ThrowIfNull(group);
        TruthTableInputValidation.ValidateArguments(inputs, outputs, biases, threshold, wavelength);
        static ImmutableArray<PinValue> Pins(ComponentGroup group, IReadOnlyList<string> names, string parameter) =>
            TruthTableInputValidation.ResolvePins(group, names, parameter)
                .Select(pin => new PinValue(pin.Name, pin.InternalPin.LogicalPin!.IDInFlow,
                    pin.InternalPin.LogicalPin.IDOutFlow)).ToImmutableArray();
        var inputValues = Pins(group, inputs, "inputPinNames");
        var outputValues = Pins(group, outputs, "outputPinNames");
        var biasValues = Pins(group, biases, "biasPins");
        // Extraction historically materialized the group's connectable pins via ComputeSMatrix.
        // Preserve that caller-thread effect without evaluating a transitive group matrix, and
        // retain existing physical-pin identities when connections already reference them.
        if (group.PhysicalPins.Count != group.ExternalPins.Count
            || group.PhysicalPins.Where((pin, index) => pin.Name != group.ExternalPins[index].Name
                || !ReferenceEquals(pin.LogicalPin, group.ExternalPins[index].InternalPin.LogicalPin)).Any())
            group.SyncPhysicalPinsFromExternalPins();
        var components = new ComponentListTileManager();
        components.AddComponent(group);
        var sources = new PhysicalExternalPortManager();
        foreach (var pin in inputValues.Concat(biasValues))
            sources.AddLightSource(new ExternalInput(pin.Name, new LaserType(LightColor.Red, wavelength),
                0, Complex.One, true), pin.In);
        // Like the legacy isolated extraction: frozen path loss is retained, propagation phase is off.
        var grid = GridManager.CreateForSimulation(components, new WaveguideConnectionManager(new WaveguideRouter()), sources);
        return new(group.GroupName, inputValues, outputValues, biasValues, threshold, wavelength,
            OpticalCircuitSnapshot.Capture(grid));
    }

    /// <summary>Builds private evaluation matrices and solves every binary input pattern.</summary>
    public async Task<TruthTable> SolveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var components = _circuit.RebuildComponents(cancellationToken);
        var rows = new List<TruthTableRow>(1 << _inputs.Length);
        for (var pattern = 0; pattern < 1 << _inputs.Length; pattern++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matrix = _circuit.BuildMatrix(components, _wavelength);
            var fieldsIn = MathNet.Numerics.LinearAlgebra.Vector<Complex>.Build.Dense(matrix.SMat.RowCount);
            void Drive(PinValue pin)
            {
                if (matrix.PinReference.TryGetValue(pin.In, out var index)) fieldsIn[index] = Complex.One;
            }
            foreach (var bias in _biases) Drive(bias);
            var bits = new Dictionary<string, bool>(_inputs.Length);
            for (var bit = 0; bit < _inputs.Length; bit++)
            {
                var on = (pattern & (1 << bit)) != 0;
                bits.Add(_inputs[bit].Name, on);
                if (on) Drive(_inputs[bit]);
            }
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var fields = await matrix.CalcFieldAtPinsAfterStepsAsync(fieldsIn, SMatrix.DefaultMaxIterations, stop)
                .ConfigureAwait(false) ?? new Dictionary<Guid, Complex>();
            cancellationToken.ThrowIfCancellationRequested();
            var outputs = new Dictionary<string, LogicOutputValue>(_outputs.Length);
            foreach (var pin in _outputs)
            {
                var power = fields.TryGetValue(pin.Out, out var field) ? field.Magnitude * field.Magnitude : 0;
                outputs.Add(pin.Name, new(power >= _threshold, power));
            }
            rows.Add(new TruthTableRow(bits, outputs));
        }
        return new TruthTable(_name, _inputs.Select(pin => pin.Name).ToArray(), _outputs.Select(pin => pin.Name).ToArray(),
            _biases.Select(pin => pin.Name).ToArray(), _threshold, _wavelength, rows);
    }
}
