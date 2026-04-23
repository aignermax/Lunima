using System.Numerics;

namespace CAP_DataAccess.Import;

/// <summary>
/// Result of parsing an external S-parameter file (Lumerical, Touchstone, etc.).
/// Wavelengths are stored in nanometers; S-matrix entries are complex numbers.
/// </summary>
public class ImportedSParameters
{
    /// <summary>Detected tool/format used to produce this data (e.g. "Lumerical SiEPIC", "Touchstone S2P").</summary>
    public string SourceFormat { get; set; } = string.Empty;

    /// <summary>Absolute path to the source file.</summary>
    public string SourceFilePath { get; set; } = string.Empty;

    /// <summary>Number of ports in the S-matrix.</summary>
    public int PortCount { get; set; }

    /// <summary>Ordered list of port names matching matrix row/column indices.</summary>
    public List<string> PortNames { get; set; } = new();

    /// <summary>
    /// S-matrix at each wavelength (nm).
    /// Key = wavelength in nm; Value = PortCount×PortCount complex matrix in row-major order.
    /// </summary>
    public Dictionary<int, Complex[,]> SMatricesByWavelengthNm { get; set; } = new();

    /// <summary>Optional metadata (polarization, simulation settings, etc.).</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}
