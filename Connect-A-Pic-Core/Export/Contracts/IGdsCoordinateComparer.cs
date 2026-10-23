namespace CAP_Core.Export.Contracts;

/// <summary>Compares exported coordinate documents and reports numerical deviations.</summary>
public interface IGdsCoordinateComparer
{
    Task<GdsCoordinateComparisonResult> CompareAsync(
        string referenceJsonPath, string systemJsonPath, string? scriptPath = null);
}
