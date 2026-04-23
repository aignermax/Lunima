using System.Globalization;
using System.Numerics;
using System.Text;
using CAP_Core.Components.Core;

namespace CAP_Core.Export;

/// <summary>
/// Emits per-component S-matrix data as a Python wavelength-indexed dictionary
/// of numpy arrays. Extraction itself is delegated to <see cref="PirSMatrixExtractor"/>
/// so the Verilog-A and PICWave paths share the same pin-GUID lookup — this
/// class only handles the Python-specific formatting.
/// </summary>
internal static class PicWaveSMatrixEmitter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Emits a Python dictionary literal mapping wavelength (m) → numpy complex
    /// 2D array for every wavelength registered on the component. Rows are
    /// indexed by <see cref="Component.PhysicalPins"/> position so downstream
    /// readers don't need LogicalPin knowledge.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Any registered wavelength has an S-matrix that does not line up with
    /// the component's PhysicalPins (see <see cref="PirSMatrixExtractor.TryExtract"/>).
    /// </exception>
    internal static string EmitWavelengthDict(Component comp, string varName)
    {
        var sb = new StringBuilder();
        int n = comp.PhysicalPins.Count;
        sb.AppendLine($"_s_{varName} = {{");

        foreach (var wlNm in comp.WaveLengthToSMatrixMap.Keys)
        {
            var matrix = PirSMatrixExtractor.TryExtract(comp, wlNm)
                ?? throw new InvalidOperationException(
                    $"Internal inconsistency: wavelength {wlNm}nm is registered on " +
                    $"'{comp.Name}' but TryExtract returned null.");

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
