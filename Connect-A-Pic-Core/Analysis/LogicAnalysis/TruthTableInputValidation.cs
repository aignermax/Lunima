using CAP_Core.Components.Core;

namespace CAP_Core.Analysis.LogicAnalysis;

/// <summary>Validates and resolves the gate interface before capturing optical values.</summary>
internal static class TruthTableInputValidation
{
    /// <summary>Checks the pure list/threshold invariants that need no group lookup.</summary>
    internal static void ValidateArguments(
        IReadOnlyList<string> inputPinNames,
        IReadOnlyList<string> outputPinNames,
        IReadOnlyList<string> biasPins,
        double powerThreshold,
        int wavelengthNm)
    {
        if (inputPinNames == null || inputPinNames.Count == 0)
            throw new ArgumentException("At least one logic input pin is required.", nameof(inputPinNames));
        if (inputPinNames.Count > TruthTableExtractor.MaxLogicInputs)
            throw new ArgumentException(
                $"At most {TruthTableExtractor.MaxLogicInputs} logic inputs are supported ({1 << TruthTableExtractor.MaxLogicInputs} combinations); got {inputPinNames.Count}.",
                nameof(inputPinNames));
        if (outputPinNames == null || outputPinNames.Count == 0)
            throw new ArgumentException("At least one logic output pin is required.", nameof(outputPinNames));

        ThrowOnDuplicates(inputPinNames, nameof(inputPinNames));
        ThrowOnDuplicates(outputPinNames, nameof(outputPinNames));
        ThrowOnDuplicates(biasPins, nameof(biasPins));
        ThrowOnOverlap(inputPinNames, outputPinNames, nameof(outputPinNames));
        ThrowOnOverlap(inputPinNames, biasPins, nameof(biasPins));
        ThrowOnOverlap(biasPins, outputPinNames, nameof(biasPins));

        if (double.IsNaN(powerThreshold) || powerThreshold <= 0.0 || powerThreshold >= 1.0)
            throw new ArgumentOutOfRangeException(
                nameof(powerThreshold), powerThreshold,
                "Normalized power threshold must lie in the open interval (0, 1).");
        if (wavelengthNm <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(wavelengthNm), wavelengthNm, "Wavelength must be a positive number of nanometers.");
    }

    /// <summary>Maps pin names onto the group's external pins, failing clearly on unknown or unsimulatable pins.</summary>
    internal static List<GroupPin> ResolvePins(
        ComponentGroup group, IReadOnlyList<string> pinNames, string parameterName)
    {
        var pins = new List<GroupPin>(pinNames.Count);
        foreach (var name in pinNames)
        {
            var pin = group.ExternalPins.FirstOrDefault(p => p.Name == name);
            if (pin == null)
                throw new ArgumentException(
                    $"Group '{group.GroupName}' exposes no external pin named '{name}'. " +
                    $"Available pins: {string.Join(", ", group.ExternalPins.Select(p => p.Name))}.",
                    parameterName);
            if (pin.InternalPin?.LogicalPin == null)
                throw new ArgumentException(
                    $"External pin '{name}' of group '{group.GroupName}' is not bound to a simulatable component pin.",
                    parameterName);
            pins.Add(pin);
        }
        return pins;
    }

    /// <summary>Rejects duplicated pin names — one pin name, one logic wire.</summary>
    private static void ThrowOnDuplicates(IReadOnlyList<string> pinNames, string parameterName)
    {
        var duplicate = pinNames.GroupBy(n => n).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (duplicate != null)
            throw new ArgumentException($"Pin '{duplicate}' is listed more than once.", parameterName);
    }

    /// <summary>Rejects pins claimed for two roles — a pin is exactly one of input, output, or bias.</summary>
    private static void ThrowOnOverlap(IReadOnlyList<string> first, IReadOnlyList<string> second, string parameterName)
    {
        var overlap = first.Intersect(second).FirstOrDefault();
        if (overlap != null)
            throw new ArgumentException($"Pin '{overlap}' is assigned two roles in one extraction.", parameterName);
    }
}
