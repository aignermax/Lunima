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
    /// Short preview of the diagonal magnitudes (|S11|, |S22|, …) truncated at 4 entries.
    /// </summary>
    public string DiagonalPreview { get; }

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

        DiagonalPreview = BuildDiagonalPreview(entry);
    }

    private static string BuildDiagonalPreview(SMatrixWavelengthEntry entry)
    {
        if (entry.Rows == 0 || entry.Real.Count == 0)
            return string.Empty;

        int n = entry.Rows;
        var parts = new List<string>();
        int maxPreview = Math.Min(n, 4);

        for (int i = 0; i < maxPreview; i++)
        {
            int idx = i * n + i;
            if (idx >= entry.Real.Count)
                break;

            double mag = Math.Sqrt(
                entry.Real[idx] * entry.Real[idx] +
                entry.Imag[idx] * entry.Imag[idx]);

            parts.Add($"|S{i + 1}{i + 1}|={mag:F3}");
        }

        var preview = string.Join("  ", parts);
        return n > 4 ? preview + " …" : preview;
    }
}
