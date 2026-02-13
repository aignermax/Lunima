using System.Text.Json;
using System.Text.Json.Serialization;

namespace CAP_Core.Components;

/// <summary>
/// Exports component library usage statistics to JSON format.
/// </summary>
public static class LibraryStatisticsExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Serializes usage statistics to a JSON string.
    /// </summary>
    /// <param name="statistics">The library statistics to export.</param>
    /// <returns>A JSON string containing the statistics.</returns>
    public static string ExportToJson(LibraryStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);

        var records = statistics.GetUsageStatistics();
        var exportData = new StatisticsExportData
        {
            TotalComponents = statistics.GetTotalComponentCount(),
            DistinctTypes = statistics.GetDistinctTypeCount(),
            UsageRecords = records.Select(r => new UsageRecordData
            {
                Identifier = r.Identifier,
                TypeNumber = r.TypeNumber,
                Count = r.Count
            }).ToList()
        };

        return JsonSerializer.Serialize(exportData, JsonOptions);
    }

    /// <summary>
    /// Data transfer object for JSON serialization of statistics.
    /// </summary>
    private class StatisticsExportData
    {
        public int TotalComponents { get; set; }
        public int DistinctTypes { get; set; }
        public List<UsageRecordData> UsageRecords { get; set; } = new();
    }

    /// <summary>
    /// Data transfer object for a single usage record.
    /// </summary>
    private class UsageRecordData
    {
        public string Identifier { get; set; } = string.Empty;
        public int TypeNumber { get; set; }
        public int Count { get; set; }
    }
}
