using System.Collections.Immutable;
using System.Runtime.InteropServices;
using CAP_Core.Components.Core;
using CAP_Core.Components.FormulaReading;
using CAP_Core.Components.Parametric;
using CAP_Core.Grid.FormulaReading;

namespace CAP_Core.LightCalculation.Snapshots;

/// <summary>Immutable matrix data and formula descriptions; each rebuild owns fresh evaluation state.</summary>
internal sealed record ComponentMatrixSnapshot(ImmutableArray<SnapshotPin> Pins,
    ImmutableArray<SnapshotSlider> Sliders, ImmutableArray<KeyValuePair<int, MatrixDefinitionSnapshot>> Matrices)
{
    internal static ComponentMatrixSnapshot Capture(Component component)
    {
        var pins = new List<SnapshotPin>(component.PhysicalPins.Count);
        var seen = new HashSet<Guid>(component.PhysicalPins.Count);
        foreach (var part in component.Parts)
            foreach (var pin in part.Pins) AddPin(pin, pins, seen);
        foreach (var physical in component.PhysicalPins)
            if (physical.LogicalPin is { } pin) AddPin(pin, pins, seen);
        var sourceSliders = component.GetAllSliders();
        var sliders = sourceSliders.Count == 0 ? Array.Empty<SnapshotSlider>() : new SnapshotSlider[sourceSliders.Count];
        for (var sliderIndex = 0; sliderIndex < sliders.Length; sliderIndex++)
        {
            var slider = sourceSliders[sliderIndex];
            sliders[sliderIndex] = new(slider.ID, slider.Number, slider.Value, slider.MinValue, slider.MaxValue);
        }
        var matrices = new KeyValuePair<int, MatrixDefinitionSnapshot>[component.WaveLengthToSMatrixMap.Count];
        var index = 0;
        foreach (var pair in component.WaveLengthToSMatrixMap)
            matrices[index++] = new(pair.Key, MatrixDefinitionSnapshot.Capture(pair.Value));
        return new(ImmutableCollectionsMarshal.AsImmutableArray(pins.ToArray()),
            ImmutableCollectionsMarshal.AsImmutableArray(sliders), ImmutableCollectionsMarshal.AsImmutableArray(matrices));
    }

    private static void AddPin(Pin pin, List<SnapshotPin> values, HashSet<Guid> seen)
    {
        if (seen.Add(pin.IDInFlow)) values.Add(new(pin.Name, pin.PinNumber, pin.IDInFlow, pin.IDOutFlow));
    }

    internal Dictionary<int, SMatrix> Rebuild() => Matrices.ToDictionary(pair => pair.Key,
        pair => pair.Value.Rebuild(Pins, Sliders));
}

internal readonly record struct SnapshotPin(string Name, int Number, Guid In, Guid Out);
internal readonly record struct SnapshotSlider(Guid Id, int Number, double Value, double Minimum, double Maximum);
internal readonly record struct SnapshotFormula(Guid From, Guid To, string Expression);

internal sealed record MatrixDefinitionSnapshot(ImmutableArray<Guid> FlowIds,
    ImmutableArray<KeyValuePair<Guid, double>> SliderValues, MatrixValuesSnapshot Numeric,
    ImmutableArray<SnapshotFormula> Formulas, ImmutableArray<ParameterDefinition> Parameters,
    ImmutableArray<FormulaConnection> ParametricConnections)
{
    internal static MatrixDefinitionSnapshot Capture(SMatrix matrix)
    {
        if (matrix.ParametricRebuild != null && matrix.ParametricSnapshot == null)
            throw new InvalidDataException("A custom matrix factory requires a detached parametric definition.");
        var ids = new Guid[matrix.PinReference.Count];
        foreach (var pair in matrix.PinReference) ids[pair.Value] = pair.Key;
        var formulas = matrix.NonLinearConnections.Count == 0 ? Array.Empty<SnapshotFormula>()
            : new SnapshotFormula[matrix.NonLinearConnections.Count];
        var index = 0;
        foreach (var pair in matrix.NonLinearConnections)
            formulas[index++] = new(pair.Key.PinIdStart, pair.Key.PinIdEnd, pair.Value.ConnectionsFunctionRaw);
        var definition = matrix.ParametricSnapshot;
        var parameters = definition?.Parameters.Count is > 0
            ? new ParameterDefinition[definition.Parameters.Count] : Array.Empty<ParameterDefinition>();
        for (index = 0; index < parameters.Length; index++)
            parameters[index] = ParametricDefinitionCopy.Copy(definition!.Parameters[index]);
        var connections = definition?.Connections.Count is > 0
            ? new FormulaConnection[definition.Connections.Count] : Array.Empty<FormulaConnection>();
        for (index = 0; index < connections.Length; index++)
            connections[index] = ParametricDefinitionCopy.Copy(definition!.Connections[index]);
        return new(ImmutableCollectionsMarshal.AsImmutableArray(ids), matrix.SliderReference.ToImmutableArray(),
            MatrixValuesSnapshot.Capture(matrix.SMat), ImmutableCollectionsMarshal.AsImmutableArray(formulas),
            ImmutableCollectionsMarshal.AsImmutableArray(parameters), ImmutableCollectionsMarshal.AsImmutableArray(connections));
    }

    internal SMatrix Rebuild(ImmutableArray<SnapshotPin> pinValues, ImmutableArray<SnapshotSlider> sliderValues)
    {
        var matrix = new SMatrix(FlowIds.ToList(), SliderValues.Select(pair => (pair.Key, pair.Value)).ToList());
        matrix.SetValues(Numeric.Entries().ToDictionary(value => (FlowIds[value.Column], FlowIds[value.Row]), value => value.Value));
        SMatrix? parametric = null;
        if (!ParametricConnections.IsEmpty)
        {
            // Build only private lightweight pins/sliders. Component.Clone() would change identities
            // and invoke constructor subscriptions; retaining ParametricRebuild would retain closures.
            var pins = pinValues.Select(pin => new Pin(pin.Name, pin.Number, default, default, pin.In, pin.Out)).ToList();
            var sliders = sliderValues.Select(slider => new Slider(slider.Id, slider.Number,
                matrix.SliderReference.GetValueOrDefault(slider.Id, slider.Value), slider.Maximum, slider.Minimum)).ToList();
            parametric = ParametricSMatrixFactory.Build(pins, sliders,
                new ParametricSMatrixSnapshot(Parameters, ParametricConnections));
        }
        // Match MathExpressionReader's existing last-wins number maps; named parametric ports
        // can legitimately share a legacy PinNumber while remaining distinct by name/flow ID.
        var pinMap = new Dictionary<string, Guid>();
        foreach (var pin in pinValues) pinMap[$"PIN{pin.Number}"] = pin.In;
        var sliderMap = new Dictionary<string, Guid>();
        foreach (var slider in sliderValues) sliderMap[$"SLIDER{slider.Number}"] = slider.Id;
        foreach (var formula in Formulas)
        {
            var key = (formula.From, formula.To);
            ConnectionFunction rebuilt;
            if (parametric != null && parametric.NonLinearConnections.TryGetValue(key, out var candidate)
                && candidate.ConnectionsFunctionRaw == formula.Expression)
                rebuilt = candidate;
            else
                rebuilt = MathExpressionReader.ConvertToDelegate(formula.Expression, pinMap, sliderMap);
            matrix.NonLinearConnections.Add(key, rebuilt);
        }
        return matrix;
    }
}
