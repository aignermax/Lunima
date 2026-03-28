using System.Numerics;
using CAP_Core.Components.Core;
using MathNet.Numerics.LinearAlgebra;
using MathNetVector = MathNet.Numerics.LinearAlgebra.Vector<System.Numerics.Complex>;

namespace CAP_Core.LightCalculation.PowerFlow;

/// <summary>
/// Computes field amplitudes at internal pins of a ComponentGroup by propagating
/// known boundary conditions (external pin field values from the global simulation)
/// through the group's full internal S-Matrix structure.
///
/// This resolves the "uniform color" visualization bug where frozen waveguide paths
/// inside a group would all display the same color, because their internal pin GUIDs
/// are absent from the global simulation field results (they are collapsed into the
/// group's external-only S-Matrix). This calculator re-expands the internal structure
/// to compute per-path field amplitudes accurately.
/// </summary>
public class InternalFieldCalculator
{
    private readonly ComponentGroupSMatrixBuilder _builder;

    /// <summary>
    /// Creates an InternalFieldCalculator using a shared S-Matrix builder instance.
    /// </summary>
    public InternalFieldCalculator(ComponentGroupSMatrixBuilder builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    /// <summary>
    /// Creates an InternalFieldCalculator with a new S-Matrix builder.
    /// </summary>
    public InternalFieldCalculator() : this(new ComponentGroupSMatrixBuilder()) { }

    /// <summary>
    /// Computes field amplitudes for all internal pins of a ComponentGroup by propagating
    /// the known external pin amplitudes through the group's internal structure.
    ///
    /// Iterates all wavelengths supported by child components and takes the maximum
    /// amplitude per pin across wavelengths, consistent with how SimulationService
    /// merges multi-wavelength results.
    /// </summary>
    /// <param name="group">The ComponentGroup whose internal fields to compute.</param>
    /// <param name="fieldResults">
    ///     Global simulation field results. Must contain amplitudes for the group's
    ///     external pins (GroupPin.InternalPin logical pin GUIDs).
    /// </param>
    /// <returns>
    ///     Dictionary mapping internal pin GUIDs (both IDInFlow and IDOutFlow) to
    ///     their computed complex field amplitudes. Empty if no child matrices are available.
    /// </returns>
    public Dictionary<Guid, Complex> ComputeInternalFields(
        ComponentGroup group,
        IReadOnlyDictionary<Guid, Complex> fieldResults)
    {
        if (group == null) throw new ArgumentNullException(nameof(group));
        if (fieldResults == null) throw new ArgumentNullException(nameof(fieldResults));

        var wavelengths = _builder.GetSupportedWavelengths(group);
        var result = new Dictionary<Guid, Complex>();

        foreach (var wl in wavelengths)
        {
            var wlFields = ComputeForWavelength(group, fieldResults, wl);
            MergeByMaxMagnitude(result, wlFields);
        }

        return result;
    }

    /// <summary>
    /// Propagates boundary conditions through the group's full internal matrix
    /// for a single wavelength.
    /// </summary>
    private Dictionary<Guid, Complex> ComputeForWavelength(
        ComponentGroup group,
        IReadOnlyDictionary<Guid, Complex> fieldResults,
        int wavelengthNm)
    {
        var fullMatrix = _builder.BuildFullInternalMatrix(group, wavelengthNm);
        if (fullMatrix == null)
            return new Dictionary<Guid, Complex>();

        var n = fullMatrix.PinReference.Count;
        var inputVector = MathNetVector.Build.Dense(n);

        // Seed input from known external pin amplitudes (light entering the group from outside)
        foreach (var extPin in group.ExternalPins)
        {
            var logicalPin = extPin.InternalPin?.LogicalPin;
            if (logicalPin == null) continue;

            if (fullMatrix.PinReference.TryGetValue(logicalPin.IDInFlow, out int inIdx) &&
                fieldResults.TryGetValue(logicalPin.IDInFlow, out var inAmp))
                inputVector[inIdx] = inAmp;

            if (fullMatrix.PinReference.TryGetValue(logicalPin.IDOutFlow, out int outIdx) &&
                fieldResults.TryGetValue(logicalPin.IDOutFlow, out var outAmp))
                inputVector[outIdx] = outAmp;
        }

        // Propagate: result = (I + T) * input, where T is the transitive matrix.
        // The identity term preserves boundary pin values; T propagates them inward.
        var propagated = fullMatrix.SMat * inputVector + inputVector;

        return ConvertToFieldResults(fullMatrix, propagated);
    }

    /// <summary>
    /// Converts the propagated field vector back to a GUID-keyed dictionary.
    /// Only includes entries with non-zero amplitude.
    /// </summary>
    private static Dictionary<Guid, Complex> ConvertToFieldResults(
        SMatrix matrix,
        MathNetVector fieldVector)
    {
        var result = new Dictionary<Guid, Complex>();

        foreach (var (pinId, idx) in matrix.PinReference)
        {
            var val = fieldVector[idx];
            if (val != Complex.Zero)
                result[pinId] = val;
        }

        return result;
    }

    /// <summary>
    /// Merges source entries into target, keeping the entry with the higher magnitude
    /// when a key already exists. This ensures the brightest computed value wins
    /// when multiple wavelengths are considered.
    /// </summary>
    private static void MergeByMaxMagnitude(
        Dictionary<Guid, Complex> target,
        Dictionary<Guid, Complex> source)
    {
        foreach (var (key, value) in source)
        {
            if (!target.TryGetValue(key, out var existing) ||
                value.Magnitude > existing.Magnitude)
                target[key] = value;
        }
    }
}
