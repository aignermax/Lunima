namespace CAP_Core.Routing.Diagnostics;

/// <summary>
/// Top-level diagnostics report structure for JSON serialization.
/// </summary>
public class DiagnosticsReport
{
    /// <summary>
    /// ISO 8601 timestamp when the report was generated.
    /// </summary>
    public string Timestamp { get; set; } = "";

    /// <summary>
    /// Total number of failed routes in this report.
    /// </summary>
    public int TotalFailedRoutes { get; set; }

    /// <summary>
    /// Individual failed route diagnostic entries.
    /// </summary>
    public List<RouteDiagnosticEntry> FailedRoutes { get; set; } = new();
}

/// <summary>
/// Serializable entry for a single failed route.
/// </summary>
public class RouteDiagnosticEntry
{
    /// <summary>Start pin name.</summary>
    public string StartPin { get; set; } = "";

    /// <summary>End pin name.</summary>
    public string EndPin { get; set; } = "";

    /// <summary>Start position in micrometers.</summary>
    public PositionEntry StartPosition { get; set; } = new();

    /// <summary>End position in micrometers.</summary>
    public PositionEntry EndPosition { get; set; } = new();

    /// <summary>Start angle in degrees.</summary>
    public double StartAngleDegrees { get; set; }

    /// <summary>End angle in degrees.</summary>
    public double EndAngleDegrees { get; set; }

    /// <summary>Human-readable failure reason.</summary>
    public string FailureReason { get; set; } = "";

    /// <summary>A* search statistics.</summary>
    public SearchStatsEntry? SearchStats { get; set; }

    /// <summary>Pin alignment analysis.</summary>
    public AlignmentEntry? PinAlignment { get; set; }

    /// <summary>Number of blocked cells in the obstacle map.</summary>
    public int ObstacleMapCellCount { get; set; }

    /// <summary>Blocked cells in the obstacle map region.</summary>
    public List<ObstacleCellEntry> ObstacleMap { get; set; } = new();
}

/// <summary>
/// Serializable 2D position.
/// </summary>
public class PositionEntry
{
    /// <summary>X coordinate in micrometers.</summary>
    public double X { get; set; }

    /// <summary>Y coordinate in micrometers.</summary>
    public double Y { get; set; }
}

/// <summary>
/// Serializable A* search statistics.
/// </summary>
public class SearchStatsEntry
{
    /// <summary>Nodes expanded during search.</summary>
    public int NodesExpanded { get; set; }

    /// <summary>Maximum nodes allowed before timeout.</summary>
    public int MaxNodesAllowed { get; set; }

    /// <summary>Elapsed time in milliseconds.</summary>
    public double ElapsedMs { get; set; }

    /// <summary>Whether the search timed out.</summary>
    public bool TimedOut { get; set; }

    /// <summary>Whether a path was found.</summary>
    public bool PathFound { get; set; }
}

/// <summary>
/// Serializable pin alignment information.
/// </summary>
public class AlignmentEntry
{
    /// <summary>Euclidean distance between pins in micrometers.</summary>
    public double DistanceMicrometers { get; set; }

    /// <summary>Forward distance from start pin in micrometers.</summary>
    public double ForwardDistanceMicrometers { get; set; }

    /// <summary>Lateral offset perpendicular to start direction in micrometers.</summary>
    public double LateralOffsetMicrometers { get; set; }

    /// <summary>Angle difference in degrees.</summary>
    public double AngleDifferenceDegrees { get; set; }

    /// <summary>Whether pins are collinear.</summary>
    public bool AreCollinear { get; set; }

    /// <summary>Whether pins face each other.</summary>
    public bool AreFacing { get; set; }
}

/// <summary>
/// Serializable obstacle cell entry.
/// </summary>
public class ObstacleCellEntry
{
    /// <summary>Grid X coordinate.</summary>
    public int X { get; set; }

    /// <summary>Grid Y coordinate.</summary>
    public int Y { get; set; }

    /// <summary>Cell state: 1 = component, 2 = waveguide.</summary>
    public byte State { get; set; }
}
