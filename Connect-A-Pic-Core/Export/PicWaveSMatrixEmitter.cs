using System.Globalization;
using System.Numerics;
using System.Text;
using CAP_Core.Components.Core;

namespace CAP_Core.Export;

/// <summary>
/// Extracts S-parameters from a component using its <see cref="PhysicalPin.LogicalPin"/>
/// GUIDs (matching <see cref="VerilogAModuleWriter.ExtractSParameters"/>'s convention)
/// and emits them as a Python wavelength-indexed dictionary of numpy arrays.
/// </summary>
internal static class PicWaveSMatrixEmitter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Returns an (out, in) → complex map for the component at the given wavelength,
    /// or <c>null</c> when no S-matrix is registered for that wavelength.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A pin has no <c>LogicalPin</c>, or the S-matrix exists but none of its
    /// (in-flow, out-flow) GUID pairs match the PhysicalPins' LogicalPins.
    /// </exception>
    internal static Dictionary<(int Out, int In), Complex>? ExtractAt(Component comp, int wavelengthNm)
    {
        var pins = comp.PhysicalPins;
        foreach (var pin in pins)
        {
            if (pin.LogicalPin == null)
            {
                throw new InvalidOperationException(
                    $"Pin '{pin.Name}' on component '{comp.Name}' has no LogicalPin. " +
                    "The component model is incomplete — finish wiring the LogicalPins " +
                    "before exporting to PICWave.");
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

        // S-matrix was registered but none of its keys line up with the
        // component's physical pins — would silently emit a zero matrix.
        if (result.Count == 0)
            throw new InvalidOperationException(
                $"Component '{comp.Name}' has an S-matrix at {wavelengthNm}nm but none of its " +
                "entries match the PhysicalPin LogicalPin GUIDs. The SMatrix pin IDs are out of " +
                "sync with the component's LogicalPins — re-wire the pins or rebuild the model.");

        return result;
    }

    /// <summary>
    /// Emits a Python dictionary literal mapping wavelength (m) → numpy complex
    /// 2D array for every wavelength registered on the component. Uses the
    /// PhysicalPins-indexed matrix so downstream readers can map rows to ports
    /// without needing LogicalPin knowledge.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Any registered wavelength has an S-matrix that does not line up with
    /// the component's PhysicalPins (see <see cref="ExtractAt"/>).
    /// </exception>
    internal static string EmitWavelengthDict(Component comp, string varName)
    {
        var sb = new StringBuilder();
        int n = comp.PhysicalPins.Count;
        sb.AppendLine($"_s_{varName} = {{");

        foreach (var wlNm in comp.WaveLengthToSMatrixMap.Keys)
        {
            var matrix = ExtractAt(comp, wlNm)
                ?? throw new InvalidOperationException(
                    $"Internal inconsistency: wavelength {wlNm}nm was registered on " +
                    $"'{comp.Name}' but ExtractAt returned null.");

            double wlM = wlNm * 1e-9;
            sb.AppendLine($"    {wlM.ToString("G6", Inv)}: np.array([");
            for (int r = 0; r < n; r++)
            {
                var row = new StringBuilder();
                for (int c = 0; c < n; c++)
                {
                    if (c > 0) row.Append(", ");
                    var v = matrix.TryGetValue((r, c), out var cv) ? cv : Complex.Zero;
                    var sign = v.Imaginary >= 0 ? "+" : "";
                    row.Append($"{v.Real.ToString("G6", Inv)}{sign}{v.Imaginary.ToString("G6", Inv)}j");
                }
                sb.AppendLine($"        [{row}],");
            }
            sb.AppendLine("    ]),");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }
}
