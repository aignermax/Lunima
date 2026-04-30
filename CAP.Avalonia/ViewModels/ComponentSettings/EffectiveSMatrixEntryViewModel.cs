using System.Globalization;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;

namespace CAP.Avalonia.ViewModels.ComponentSettings;

/// <summary>
/// Read-only display row for the "Currently effective S-matrix" section in the
/// Component Settings dialog. Shows whatever S-matrix the simulator will use
/// for a given wavelength right now — i.e. the PDK default unless an override
/// has replaced it. Source-tagged so the user can tell which layer wins
/// without cross-referencing the override list.
/// </summary>
public class EffectiveSMatrixEntryViewModel
{
    /// <summary>Wavelength label (e.g. "1550 nm").</summary>
    public string WavelengthLabel { get; }

    /// <summary>Matrix dimensions string (e.g. "4 × 4").</summary>
    public string Dimensions { get; }

    /// <summary>
    /// Source-of-truth tag — "PDK Default" when no override is in play for this
    /// wavelength, "Override active" otherwise. The dialog wraps the row in a
    /// faintly tinted background based on this tag so the visual difference is
    /// obvious at a glance.
    /// </summary>
    public string SourceTag { get; }

    /// <summary>True when <see cref="SourceTag"/> is "Override active".</summary>
    public bool IsOverridden { get; }

    /// <summary>
    /// Diagonal magnitudes preview (|S11|, |S22|, …) for the first four ports.
    /// </summary>
    public string DiagonalPreview { get; }

    /// <summary>
    /// Builds an entry from a live <see cref="SMatrix"/> and the component's
    /// physical pin list. Reads the pin-keyed transfer values via the
    /// SMatrix.PinReference index map; if any expected pin id is missing the
    /// preview falls back to the empty string rather than throwing — the
    /// dialog should never crash because of an unusual S-matrix shape.
    /// </summary>
    public EffectiveSMatrixEntryViewModel(
        int wavelengthNm,
        SMatrix sMatrix,
        IReadOnlyList<Pin> pins,
        bool isOverridden)
    {
        WavelengthLabel = $"{wavelengthNm} nm";
        Dimensions = $"{pins.Count} × {pins.Count}";
        IsOverridden = isOverridden;
        SourceTag = isOverridden ? "Override active" : "PDK Default";
        DiagonalPreview = BuildDiagonalPreview(sMatrix, pins);
    }

    private static string BuildDiagonalPreview(SMatrix sMatrix, IReadOnlyList<Pin> pins)
    {
        if (pins.Count == 0)
            return string.Empty;

        var parts = new List<string>();
        int maxPreview = Math.Min(pins.Count, 4);

        for (int i = 0; i < maxPreview; i++)
        {
            // S[out, in] convention — diagonal = |S(pin_i_out, pin_i_in)|.
            // A missing pin id (rare but possible for unusual templates)
            // skips that diagonal entry rather than failing the whole row.
            if (!sMatrix.PinReference.TryGetValue(pins[i].IDOutFlow, out var outIdx))
                continue;
            if (!sMatrix.PinReference.TryGetValue(pins[i].IDInFlow, out var inIdx))
                continue;

            var v = sMatrix.SMat[outIdx, inIdx];
            double mag = Math.Sqrt(v.Real * v.Real + v.Imaginary * v.Imaginary);
            // Invariant culture so the "0.950" form is stable on de-DE/fr-FR locales
            // — those would otherwise render "0,950" and break round-trip / log parsing.
            parts.Add($"|S{i + 1}{i + 1}|={mag.ToString("F3", CultureInfo.InvariantCulture)}");
        }

        var preview = string.Join("  ", parts);
        return pins.Count > 4 ? preview + " …" : preview;
    }
}
