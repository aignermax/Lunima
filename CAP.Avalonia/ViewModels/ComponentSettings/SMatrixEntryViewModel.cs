using System.Globalization;
using CAP_DataAccess.Persistence.PIR;

namespace CAP.Avalonia.ViewModels.ComponentSettings;

/// <summary>
/// Display model for a single wavelength entry in the Component Settings dialog.
/// Shows the wavelength key, matrix dimensions, port names, and a magnitude preview.
/// </summary>
public class SMatrixEntryViewModel
{
    /// <summary>Wavelength in nm as stored in the dictionary key (e.g. "1550").</summary>
    public string WavelengthKey { get; }

    /// <summary>Formatted wavelength label for the UI (e.g. "1550 nm").</summary>
    public string WavelengthLabel { get; }

    /// <summary>Matrix dimensions string (e.g. "4 × 4").</summary>
    public string Dimensions { get; }

    /// <summary>Comma-separated port names, or "(no port names)" when absent.</summary>
    public string PortNamesDisplay { get; }

    /// <summary>Optional source note from the importer (e.g. "Lumerical FDTD").</summary>
    public string? SourceNote { get; }

    /// <summary>
    /// Short preview of the strongest off-diagonal couplings — i.e. the largest
    /// |S_ij| transmissions between distinct ports, formatted as "P_i→P_j=value".
    /// We deliberately skip the diagonal: |S_ii| is the reflection at port i,
    /// which is engineered to be ≈0 for passive photonic devices (couplers,
    /// splitters, MMIs) and so was the least informative thing to preview.
    /// Top 4 couplings shown, sorted by magnitude desc.
    /// </summary>
    public string MagnitudePreview { get; }

    /// <summary>
    /// Initialises the entry from the raw DTO and optional source note.
    /// </summary>
    public SMatrixEntryViewModel(string wavelengthKey, SMatrixWavelengthEntry entry, string? sourceNote)
    {
        WavelengthKey = wavelengthKey;
        WavelengthLabel = $"{wavelengthKey} nm";
        Dimensions = $"{entry.Rows} × {entry.Cols}";
        SourceNote = sourceNote;

        PortNamesDisplay = entry.PortNames != null && entry.PortNames.Count > 0
            ? string.Join(", ", entry.PortNames)
            : "(no port names)";

        MagnitudePreview = BuildMagnitudePreview(entry);
    }

    private static string BuildMagnitudePreview(SMatrixWavelengthEntry entry)
    {
        if (entry.Rows == 0 || entry.Real.Count == 0)
            return string.Empty;

        int n = entry.Rows;
        var couplings = new List<(int From, int To, double Magnitude)>();

        // Convention: entry.Real[r*n + c] = S[r=out, c=in]. We want
        // "input port from → output port to" labels, so iterate (in=from, out=to).
        for (int from = 0; from < n; from++)
        {
            for (int to = 0; to < n; to++)
            {
                if (from == to) continue; // skip reflection (always ≈0 for passive devices)
                int idx = to * n + from;
                if (idx >= entry.Real.Count) continue;

                double mag = Math.Sqrt(
                    entry.Real[idx] * entry.Real[idx] +
                    entry.Imag[idx] * entry.Imag[idx]);
                if (mag < 1e-6) continue; // hide noise floor

                couplings.Add((from, to, mag));
            }
        }

        if (couplings.Count == 0)
            return "(no significant couplings)";

        couplings.Sort((a, b) => b.Magnitude.CompareTo(a.Magnitude));
        var top = couplings.Take(4)
            // InvariantCulture so the "0.707" form is stable on de-DE / fr-FR locales.
            .Select(c => $"P{c.From + 1}→P{c.To + 1}={c.Magnitude.ToString("F3", CultureInfo.InvariantCulture)}");

        var preview = string.Join("  ", top);
        return couplings.Count > 4 ? preview + " …" : preview;
    }
}
