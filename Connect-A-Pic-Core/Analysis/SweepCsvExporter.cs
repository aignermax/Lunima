namespace CAP_Core.Analysis;

/// <summary>Writes parameter sweep results to CSV files.</summary>
public static class SweepCsvExporter
{
    public static void ExportToFile(SweepResult result, string filePath)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be empty.", nameof(filePath));

        File.WriteAllText(filePath, SweepCsvFormatter.GenerateCsvContent(result));
    }

    /// <summary>Compatibility entry point for the shared pure formatter.</summary>
    public static string GenerateCsvContent(SweepResult result) =>
        SweepCsvFormatter.GenerateCsvContent(result);
}
