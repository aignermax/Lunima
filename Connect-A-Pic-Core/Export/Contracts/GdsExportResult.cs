namespace CAP_Core.Export.Contracts;

/// <summary>
/// Result of a GDS export operation.
/// </summary>
public class GdsExportResult
{
    /// <summary>
    /// Path to the exported Python script.
    /// </summary>
    public string ScriptPath { get; init; } = string.Empty;

    /// <summary>
    /// Path to the generated GDS file (null if generation failed or was skipped).
    /// </summary>
    public string? GdsPath { get; init; }

    /// <summary>
    /// Status message describing the export outcome.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// True if both script and GDS were successfully created.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if export failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
