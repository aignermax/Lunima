namespace CAP_DataAccess.Persistence.PIR;

/// <summary>
/// Stores per-component S-matrix data across multiple wavelengths for .lun PIR format.
/// Keyed in the parent dictionary by component identifier string.
/// </summary>
public class ComponentSMatrixData
{
    /// <summary>Copies every matrix value before background serialization.</summary>
    public ComponentSMatrixData Copy() => new()
    {
        SourceNote = SourceNote,
        Wavelengths = Wavelengths.ToDictionary(pair => pair.Key, pair => pair.Value.Copy(), Wavelengths.Comparer)
    };

    /// <summary>
    /// S-matrix entries keyed by wavelength in nanometers (string key for JSON compatibility).
    /// Example key: "1550" for 1550 nm.
    /// </summary>
    public Dictionary<string, SMatrixWavelengthEntry> Wavelengths { get; set; } = new();

    /// <summary>
    /// Optional free-text note describing the source of this S-matrix data.
    /// Examples: "Lumerical FDTD v2024", "PDK default", "Tidy3D sweep"
    /// </summary>
    public string? SourceNote { get; set; }
}

/// <summary>
/// A single S-matrix at a specific wavelength, stored in flat row-major format.
/// The matrix is size Rows x Cols. Entry [r, c] has real part at index r*Cols+c in Real,
/// and imaginary part at the same index in Imag.
/// Port ordering follows the component's PhysicalPins list order.
/// </summary>
public class SMatrixWavelengthEntry
{
    /// <summary>Copies matrix arrays and port ordering without changing their representation.</summary>
    public SMatrixWavelengthEntry Copy() => new()
    {
        Rows = Rows, Cols = Cols, Real = [.. Real], Imag = [.. Imag],
        PortNames = PortNames is null ? null : [.. PortNames]
    };

    /// <summary>Number of rows (= number of ports).</summary>
    public int Rows { get; set; }

    /// <summary>Number of columns (= number of ports).</summary>
    public int Cols { get; set; }

    /// <summary>
    /// Real parts of all matrix entries in row-major order.
    /// Length must equal Rows * Cols.
    /// </summary>
    public List<double> Real { get; set; } = new();

    /// <summary>
    /// Imaginary parts of all matrix entries in row-major order.
    /// Length must equal Rows * Cols.
    /// </summary>
    public List<double> Imag { get; set; } = new();

    /// <summary>
    /// Port names in the same order used for matrix rows/columns.
    /// Allows reconstruction of pin-to-index mapping even if pin order changes.
    /// </summary>
    public List<string>? PortNames { get; set; }
}
