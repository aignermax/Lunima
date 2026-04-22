namespace CAP_DataAccess.Persistence.PIR;

/// <summary>
/// A reference to an external simulation file or measurement dataset linked to this design.
/// Allows the .lun file to point to Lumerical, Tidy3D, measurement, or fabrication data
/// without embedding the (potentially large) raw data.
/// </summary>
public class ExternalReferenceData
{
    /// <summary>
    /// Identifier of the component this reference applies to.
    /// Use "*" to indicate a design-level reference not tied to a specific component.
    /// </summary>
    public string ComponentIdentifier { get; set; } = "";

    /// <summary>
    /// The external tool or data source type.
    /// Known values: "tidy3d", "lumerical", "measurement", "fabrication", "sparameters"
    /// </summary>
    public string Tool { get; set; } = "";

    /// <summary>
    /// File path or URI for the referenced data.
    /// May be absolute, relative to the .lun file, or a remote URI.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Optional SHA-256 hash of the referenced file for integrity verification.
    /// Format: "sha256:&lt;hex-digest&gt;"
    /// Null if no hash verification is desired.
    /// </summary>
    public string? FileHash { get; set; }

    /// <summary>
    /// Human-readable description of what this reference contains.
    /// Example: "FDTD simulation of custom MMI coupler at 1550 nm"
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// ISO 8601 date-time when this reference was last verified or imported (UTC).
    /// Null if never verified.
    /// </summary>
    public string? LastVerified { get; set; }
}
