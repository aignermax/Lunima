namespace CAP_DataAccess.Persistence.PIR;

/// <summary>
/// Design metadata stored in the .lun PIR format.
/// Captures PDK version references, design rule constraints, and authorship history.
/// </summary>
public class DesignMetadata
{
    /// <summary>
    /// PDK versions used in this design, keyed by PDK name.
    /// Example: { "siepic-ebeam": "v1.2.0", "Built-in": "1.0" }
    /// </summary>
    public Dictionary<string, string> PdkVersions { get; set; } = new();

    /// <summary>
    /// Design rule constraints in effect for this design.
    /// Null if no explicit constraints have been set.
    /// </summary>
    public DesignRulesData? DesignRules { get; set; }

    /// <summary>
    /// Authorship and version history for this design.
    /// </summary>
    public AuthorshipData Authorship { get; set; } = new();

    /// <summary>
    /// Free-text description of this design's purpose or contents.
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Fabrication design rule constraints for the design.
/// All distance values are in micrometers.
/// </summary>
public class DesignRulesData
{
    /// <summary>
    /// Minimum bend radius for waveguides in micrometers.
    /// Null if unconstrained.
    /// </summary>
    public double? MinBendRadiusMicrometers { get; set; }

    /// <summary>
    /// Minimum spacing between adjacent waveguides in micrometers.
    /// Null if unconstrained.
    /// </summary>
    public double? MinSpacingMicrometers { get; set; }

    /// <summary>
    /// Target waveguide width in micrometers.
    /// Null if using PDK default.
    /// </summary>
    public double? WaveguideWidthMicrometers { get; set; }
}

/// <summary>
/// Authorship and version history metadata.
/// </summary>
public class AuthorshipData
{
    /// <summary>
    /// ISO 8601 date string when this design was first created.
    /// Example: "2024-01-01"
    /// Set automatically on first save; never overwritten on subsequent saves.
    /// </summary>
    public string? Created { get; set; }

    /// <summary>
    /// ISO 8601 date-time string when this design was last saved (UTC).
    /// Example: "2024-01-15T10:30:00Z"
    /// Updated automatically on every save.
    /// </summary>
    public string? Modified { get; set; }

    /// <summary>
    /// Name or identifier of the person who created this design.
    /// Null if not specified.
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Optional project or version tag for this design.
    /// Examples: "v1.0", "submission-rev3"
    /// </summary>
    public string? Version { get; set; }
}
