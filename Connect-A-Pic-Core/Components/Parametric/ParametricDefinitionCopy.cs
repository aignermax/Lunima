namespace CAP_Core.Components.Parametric;

/// <summary>Copies semantic values into base definitions, excluding caller subclass state and object graphs.</summary>
internal static class ParametricDefinitionCopy
{
    internal static ParameterDefinition Copy(ParameterDefinition value) => new(value.Name,
        value.DefaultValue, value.MinValue, value.MaxValue, value.Label, value.SliderNumber, value.Unit);

    internal static FormulaConnection Copy(FormulaConnection value) => new(value.FromPin,
        value.ToPin, value.MagnitudeFormula, value.PhaseDegFormula);
}
