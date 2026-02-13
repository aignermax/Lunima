using System.Text.Json;
using System.Text.Json.Serialization;

namespace CAP_Core.Routing.Diagnostics;

/// <summary>
/// Exports routing diagnostics to JSON format for analysis.
/// </summary>
public static class DiagnosticsExporter
{
    /// <summary>
    /// Default output file name for routing diagnostics.
    /// </summary>
    public const string DefaultFileName = ".routing-diag.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Exports failed route diagnostics to a JSON string.
    /// </summary>
    /// <param name="failedRoutes">The failed routes to export.</param>
    /// <returns>JSON string containing the diagnostics report.</returns>
    public static string ExportToJson(IReadOnlyList<FailedRouteInfo> failedRoutes)
    {
        var report = CreateReport(failedRoutes);
        return JsonSerializer.Serialize(report, JsonOptions);
    }

    /// <summary>
    /// Exports failed route diagnostics to a JSON file.
    /// </summary>
    /// <param name="failedRoutes">The failed routes to export.</param>
    /// <param name="filePath">Output file path. Defaults to .routing-diag.json.</param>
    public static void ExportToFile(
        IReadOnlyList<FailedRouteInfo> failedRoutes,
        string? filePath = null)
    {
        string path = filePath ?? DefaultFileName;
        string json = ExportToJson(failedRoutes);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Creates the diagnostic report structure from failed routes.
    /// </summary>
    private static DiagnosticsReport CreateReport(
        IReadOnlyList<FailedRouteInfo> failedRoutes)
    {
        return new DiagnosticsReport
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            TotalFailedRoutes = failedRoutes.Count,
            FailedRoutes = failedRoutes.Select(MapRoute).ToList()
        };
    }

    /// <summary>
    /// Maps a FailedRouteInfo to a serializable route entry.
    /// </summary>
    private static RouteDiagnosticEntry MapRoute(FailedRouteInfo info)
    {
        return new RouteDiagnosticEntry
        {
            StartPin = info.StartPinName,
            EndPin = info.EndPinName,
            StartPosition = new PositionEntry
            {
                X = Math.Round(info.StartPosition.X, 2),
                Y = Math.Round(info.StartPosition.Y, 2)
            },
            EndPosition = new PositionEntry
            {
                X = Math.Round(info.EndPosition.X, 2),
                Y = Math.Round(info.EndPosition.Y, 2)
            },
            StartAngleDegrees = Math.Round(info.StartAngleDegrees, 1),
            EndAngleDegrees = Math.Round(info.EndAngleDegrees, 1),
            FailureReason = info.FailureReason,
            SearchStats = MapSearchStats(info.SearchStats),
            PinAlignment = MapAlignment(info.PinAlignment),
            ObstacleMapCellCount = info.ObstacleMap.Count,
            ObstacleMap = info.ObstacleMap.Select(c => new ObstacleCellEntry
            {
                X = c.GridX,
                Y = c.GridY,
                State = c.State
            }).ToList()
        };
    }

    /// <summary>
    /// Maps search stats to a serializable entry.
    /// </summary>
    private static SearchStatsEntry? MapSearchStats(
        AStarPathfinder.AStarSearchStats? stats)
    {
        if (stats == null) return null;

        return new SearchStatsEntry
        {
            NodesExpanded = stats.NodesExpanded,
            MaxNodesAllowed = stats.MaxNodesAllowed,
            ElapsedMs = Math.Round(stats.ElapsedMilliseconds, 2),
            TimedOut = stats.TimedOut,
            PathFound = stats.PathFound
        };
    }

    /// <summary>
    /// Maps alignment info to a serializable entry.
    /// </summary>
    private static AlignmentEntry? MapAlignment(PinAlignmentInfo? alignment)
    {
        if (alignment == null) return null;

        return new AlignmentEntry
        {
            DistanceMicrometers = Math.Round(alignment.DistanceMicrometers, 2),
            ForwardDistanceMicrometers = Math.Round(
                alignment.ForwardDistanceMicrometers, 2),
            LateralOffsetMicrometers = Math.Round(
                alignment.LateralOffsetMicrometers, 2),
            AngleDifferenceDegrees = Math.Round(
                alignment.AngleDifferenceDegrees, 1),
            AreCollinear = alignment.AreCollinear,
            AreFacing = alignment.AreFacing
        };
    }
}
