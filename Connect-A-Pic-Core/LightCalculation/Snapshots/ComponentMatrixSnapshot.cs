using System.Collections.Immutable;
using CAP_Core.Components.Core;
using CAP_Core.Components.FormulaReading;
using CAP_Core.Components.Parametric;
using CAP_Core.Grid.FormulaReading;
using MathNet.Numerics.LinearAlgebra;

namespace CAP_Core.LightCalculation.Snapshots;

/// <summary>Immutable matrix data and formula descriptions; each rebuild owns fresh evaluation state.</summary>
internal sealed record ComponentMatrixSnapshot(ImmutableArray<SnapshotPin> Pins,
    ImmutableArray<SnapshotSlider> Sliders, ImmutableDictionary<int, MatrixDefinitionSnapshot> Matrices)
{
    internal static ComponentMatrixSnapshot Capture(Component component)
    {
        var pins = component.GetAllPins().Concat(component.PhysicalPins.Select(pin => pin.LogicalPin)
                .Where(pin => pin != null)).DistinctBy(pin => pin.IDInFlow)
            .Select(pin => new SnapshotPin(pin.Name, pin.PinNumber, pin.IDInFlow, pin.IDOutFlow)).ToImmutableArray();
        var sliders = component.GetAllSliders().Select(slider =>
            new SnapshotSlider(slider.ID, slider.Number, slider.Value, slider.MinValue, slider.MaxValue)).ToImmutableArray();
        return new(pins, sliders, component.WaveLengthToSMatrixMap.ToImmutableDictionary(
            pair => pair.Key, pair => MatrixDefinitionSnapshot.Capture(pair.Value)));
    }

    internal Dictionary<int, SMatrix> Rebuild() => Matrices.ToDictionary(pair => pair.Key,
        pair => pair.Value.Rebuild(Pins, Sliders));
}

internal readonly record struct SnapshotPin(string Name, int Number, Guid In, Guid Out);
internal readonly record struct SnapshotSlider(Guid Id, int Number, double Value, double Minimum, double Maximum);
internal readonly record struct SnapshotFormula(Guid From, Guid To, string Expression);

internal sealed record MatrixDefinitionSnapshot(ImmutableArray<Guid> FlowIds,
    ImmutableDictionary<Guid, double> SliderValues, ImmutableArray<SimulationTransfer> Numeric,
    ImmutableArray<SnapshotFormula> Formulas, ImmutableArray<ParameterDefinition> Parameters,
    ImmutableArray<FormulaConnection> ParametricConnections)
{
    internal static MatrixDefinitionSnapshot Capture(SMatrix matrix)
    {
        if (matrix.ParametricRebuild != null && matrix.ParametricSnapshot == null)
            throw new InvalidDataException("A custom matrix factory requires a detached parametric definition.");
        var ids = matrix.PinReference.OrderBy(pair => pair.Value).Select(pair => pair.Key).ToImmutableArray();
        var numeric = matrix.SMat.EnumerateIndexed(Zeros.AllowSkip)
            .Select(entry => new SimulationTransfer(ids[entry.Item2], ids[entry.Item1], entry.Item3)).ToImmutableArray();
        return new(ids, matrix.SliderReference.ToImmutableDictionary(), numeric,
            matrix.NonLinearConnections.Select(pair => new SnapshotFormula(pair.Key.PinIdStart,
                pair.Key.PinIdEnd, pair.Value.ConnectionsFunctionRaw)).ToImmutableArray(),
            matrix.ParametricSnapshot?.Parameters.Select(value => ParametricDefinitionCopy.Copy(value)).ToImmutableArray()
                ?? ImmutableArray<ParameterDefinition>.Empty,
            matrix.ParametricSnapshot?.Connections.Select(value => ParametricDefinitionCopy.Copy(value)).ToImmutableArray()
                ?? ImmutableArray<FormulaConnection>.Empty);
    }

    internal SMatrix Rebuild(ImmutableArray<SnapshotPin> pinValues, ImmutableArray<SnapshotSlider> sliderValues)
    {
        var matrix = new SMatrix(FlowIds.ToList(), SliderValues.Select(pair => (pair.Key, pair.Value)).ToList());
        matrix.SetValues(Numeric.ToDictionary(value => (value.From, value.To), value => value.Value));
        SMatrix? parametric = null;
        if (!ParametricConnections.IsEmpty)
        {
            // Build only private lightweight pins/sliders. Component.Clone() would change identities
            // and invoke constructor subscriptions; retaining ParametricRebuild would retain closures.
            var pins = pinValues.Select(pin => new Pin(pin.Name, pin.Number, default, default, pin.In, pin.Out)).ToList();
            var sliders = sliderValues.Select(slider => new Slider(slider.Id, slider.Number,
                SliderValues.GetValueOrDefault(slider.Id, slider.Value), slider.Maximum, slider.Minimum)).ToList();
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
