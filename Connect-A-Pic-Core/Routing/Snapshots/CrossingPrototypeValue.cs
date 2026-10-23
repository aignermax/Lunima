using System.Collections.Immutable;
using CAP_Core.Components.Core;
using CAP_Core.Components.Parametric;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.MaterialDispersion;
using CAP_Core.LightCalculation.Snapshots;
using CAP_Core.Tiles;

namespace CAP_Core.Routing.Snapshots;

/// <summary>A detached PDK definition. Every build creates fresh component, pin, slider and evaluator identities.</summary>
internal sealed record CrossingPrototypeValue(RouteComponentValue Geometry, ComponentMatrixSnapshot Physics,
    string Parameters, string? Module, int Type, string? DisplayName, string? Factory, string? CrossSection,
    string? ModelStatus, double OriginX, double OriginY, double OffsetX, double OffsetY,
    ImmutableArray<ParameterDefinition> ParameterDefinitions, IDispersionModel? Dispersion)
{
    internal static CrossingPrototypeValue Capture(Component component)
    {
        if (component is ComponentGroup)
            throw new NotSupportedException("A crossing prototype must be a component, not a hierarchical group.");
        // These sealed implementations own only readonly scalars or private copied arrays. An arbitrary
        // implementation could retain live editor state, and sampling it at 1550 would lose export physics.
        if (component.WaveguideDispersion is not (null or ConstantDispersion or PolynomialDispersion or TabulatedDispersion))
            throw new NotSupportedException("A crossing prototype requires an immutable built-in dispersion definition.");
        return new(RouteComponentValue.Capture(component), ComponentMatrixSnapshot.Capture(component),
            component.NazcaFunctionParameters, component.NazcaModuleName, component.TypeNumber,
            component.HumanReadableName, component.GdsFactoryFunction, component.GdsFactoryRoutingCrossSection,
            component.SimulationModelStatus, component.NazcaOriginOffsetX, component.NazcaOriginOffsetY,
            component.PhysicalOffsetX, component.PhysicalOffsetY,
            component.ParameterDefinitions.Select(value => ParametricDefinitionCopy.Copy(value)).ToImmutableArray(),
            component.WaveguideDispersion);
    }

    internal Component Build(RouteProcessFloorTable floors)
    {
        var ids = Physics.Pins.SelectMany(pin => new[] { pin.In, pin.Out })
            .Concat(Physics.Matrices.Values.SelectMany(matrix => matrix.FlowIds)).Distinct()
            .ToDictionary(id => id, _ => Guid.NewGuid());
        var sliderIds = Physics.Sliders.Select(slider => slider.Id).Distinct().ToDictionary(id => id, _ => Guid.NewGuid());
        var pinValues = Physics.Pins.Select(pin => pin with { In = ids[pin.In], Out = ids[pin.Out] }).ToImmutableArray();
        var sliderValues = Physics.Sliders.Select(slider => slider with { Id = sliderIds[slider.Id] }).ToImmutableArray();
        var geometry = Geometry with
        {
            Id = Guid.NewGuid(),
            Pins = Geometry.Pins.Select(pin => pin with
            {
                Id = Guid.NewGuid(),
                Logical = pin.Logical is { } logical ? logical with { In = ids[logical.In], Out = ids[logical.Out] } : null
            }).ToImmutableArray()
        };
        var logicalPins = new Dictionary<Guid, Pin>();
        var shell = geometry.BuildShell(new(), logicalPins);
        foreach (var pin in pinValues)
            logicalPins.TryAdd(pin.In, new Pin(pin.Name, pin.Number, default, default, pin.In, pin.Out));
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(pinValues.Select(pin => logicalPins[pin.In]).ToList());
        var sliders = sliderValues.Select(slider => new Slider(slider.Id, slider.Number, slider.Value,
            slider.Maximum, slider.Minimum)).ToList();
        var matrices = Physics.Matrices.ToDictionary(pair => pair.Key,
            pair => RebuildMatrix(pair.Value, ids, sliderIds, pinValues, sliderValues));
        var component = new Component(matrices, sliders, Geometry.NazcaFunction, Parameters, parts,
            Type, Geometry.Name, DiscreteRotation.R0, shell.PhysicalPins);
        component.RestoreSnapshotIdentity(geometry.Id);
        geometry.ApplyGeometry(component);
        component.NazcaModuleName = Module;
        component.HumanReadableName = DisplayName;
        component.GdsFactoryFunction = Factory;
        component.GdsFactoryRoutingCrossSection = CrossSection;
        component.SimulationModelStatus = ModelStatus;
        component.NazcaOriginOffsetX = OriginX;
        component.NazcaOriginOffsetY = OriginY;
        component.PhysicalOffsetX = OffsetX;
        component.PhysicalOffsetY = OffsetY;
        component.ParameterDefinitions = ParameterDefinitions;
        component.WaveguideDispersion = Dispersion;
        // Component construction primes slider subscriptions at their midpoint. Restore the captured values.
        for (var index = 0; index < sliders.Count; index++) sliders[index].Value = sliderValues[index].Value;
        for (var index = 0; index < geometry.Pins.Length; index++)
            floors.RegisterPrototypePin(geometry.Pins[index].Id, Geometry.Pins[index].Id);
        return component;
    }

    private static SMatrix RebuildMatrix(MatrixDefinitionSnapshot original, IReadOnlyDictionary<Guid, Guid> pins,
        IReadOnlyDictionary<Guid, Guid> sliders, ImmutableArray<SnapshotPin> pinValues, ImmutableArray<SnapshotSlider> sliderValues)
    {
        var definition = original with
        {
            FlowIds = original.FlowIds.Select(id => pins[id]).ToImmutableArray(),
            SliderValues = original.SliderValues.ToImmutableDictionary(pair => sliders[pair.Key], pair => pair.Value),
            Numeric = original.Numeric.Select(value => value with { From = pins[value.From], To = pins[value.To] }).ToImmutableArray(),
            Formulas = original.Formulas.Select(value => value with { From = pins[value.From], To = pins[value.To] }).ToImmutableArray()
        };
        var matrix = definition.Rebuild(pinValues, sliderValues);
        if (!definition.ParametricConnections.IsEmpty)
        {
            // Keep a detached definition for later cloning/persistence, never the source rebuild delegate.
            var snapshot = new ParametricSMatrixSnapshot(definition.Parameters, definition.ParametricConnections);
            matrix.ParametricSnapshot = snapshot;
            matrix.ParametricRebuild = (newPins, newSliders) => ParametricSMatrixFactory.Build(newPins, newSliders, snapshot);
        }
        return matrix;
    }
}
