using System.Numerics;
using CAP_Core.Components.Core;

namespace CAP_Core.Export;

/// <summary>
/// Shared pin-GUID-based S-matrix extraction used by every PIR export target.
/// <para>
/// Takes a <see cref="Component"/> and a wavelength, returns an
/// (out-port-index, in-port-index) → complex transfer coefficient map where
/// the indices are positions in <see cref="Component.PhysicalPins"/>. Entries
/// are looked up by the pin's <c>LogicalPin.IDInFlow</c> / <c>IDOutFlow</c>
/// GUIDs against <see cref="SMatrix.GetNonNullValues"/>, so a design whose
/// PhysicalPins have been reordered still produces a correct matrix.
/// </para>
/// <para>
/// The extractor is deliberately policy-free: it returns <c>null</c> when no
/// S-matrix is registered at the given wavelength, letting each caller decide
/// whether to fall back to a heuristic (Verilog-A) or throw (PICWave / future
/// targets). Two internal inconsistencies are reported as loud exceptions —
/// no silent fallbacks here: a pin missing its <see cref="PhysicalPin.LogicalPin"/>
/// link, and a registered S-matrix whose pin GUIDs do not line up with any
/// PhysicalPin pair (a zero-matrix export would be physically misleading).
/// </para>
/// </summary>
internal static class PirSMatrixExtractor
{
    /// <summary>
    /// Returns the S-parameter map for <paramref name="comp"/> at
    /// <paramref name="wavelengthNm"/>, or <c>null</c> when the component has
    /// no S-matrix registered for that wavelength.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a <see cref="PhysicalPin"/> has no <see cref="PhysicalPin.LogicalPin"/>
    /// set, or when the registered S-matrix exists but none of its
    /// (in-flow-GUID, out-flow-GUID) keys match the component's PhysicalPins.
    /// </exception>
    internal static Dictionary<(int Out, int In), Complex>? TryExtract(
        Component comp, int wavelengthNm)
    {
        var pins = comp.PhysicalPins;

        foreach (var pin in pins)
        {
            if (pin.LogicalPin == null)
            {
                throw new InvalidOperationException(
                    $"Pin '{pin.Name}' on component '{comp.Name}' has no LogicalPin. " +
                    "The component model is incomplete — finish wiring the LogicalPins " +
                    "before exporting.");
            }
        }

        if (!comp.WaveLengthToSMatrixMap.TryGetValue(wavelengthNm, out var sMatrix))
            return null;

        var transfers = sMatrix.GetNonNullValues();
        var result = new Dictionary<(int, int), Complex>();

        for (int outIdx = 0; outIdx < pins.Count; outIdx++)
        {
            var outPin = pins[outIdx];
            for (int inIdx = 0; inIdx < pins.Count; inIdx++)
            {
                var inPin = pins[inIdx];
                var key = (inPin.LogicalPin!.IDInFlow, outPin.LogicalPin!.IDOutFlow);
                if (transfers.TryGetValue(key, out var transfer))
                    result[(outIdx, inIdx)] = transfer;
            }
        }

        // S-matrix was registered but none of its (in, out) GUID pairs line up
        // with the PhysicalPins — a silent all-zero export would mask the drift.
        if (result.Count == 0)
            throw new InvalidOperationException(
                $"Component '{comp.Name}' has an S-matrix at {wavelengthNm}nm but none of its " +
                "entries match the PhysicalPin LogicalPin GUIDs. The SMatrix pin IDs are out of " +
                "sync with the component's LogicalPins — re-wire the pins or rebuild the model.");

        return result;
    }
}
