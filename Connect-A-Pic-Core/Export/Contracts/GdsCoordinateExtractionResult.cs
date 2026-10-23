namespace CAP_Core.Export.Contracts;

/// <summary>Result of a coordinate extraction operation.</summary>
public class GdsCoordinateExtractionResult
{
    /// <summary>True if extraction completed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Path to the output JSON file.</summary>
    public string? JsonOutputPath { get; init; }

    /// <summary>Raw JSON string with extracted coordinates.</summary>
    public string? JsonContent { get; init; }

    /// <summary>Error message if extraction failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Status message for UI display.</summary>
    public string Status { get; init; } = string.Empty;
}
