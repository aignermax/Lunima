namespace CAP_Core.Export.Contracts;

/// <summary>Extracts a GDS coordinate document using the selected Python interpreter.</summary>
public interface IGdsCoordinateExtractor
{
    void SetCustomPythonPath(string? pythonPath);
    Task<GdsCoordinateExtractionResult> ExtractAsync(
        string gdsPath, string? outputJsonPath = null, CancellationToken cancellationToken = default);
}
