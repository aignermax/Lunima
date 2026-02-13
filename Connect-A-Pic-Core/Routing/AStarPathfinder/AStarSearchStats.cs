namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Statistics collected during an A* pathfinding search.
/// </summary>
public class AStarSearchStats
{
    /// <summary>
    /// Number of nodes expanded during the search.
    /// </summary>
    public int NodesExpanded { get; set; }

    /// <summary>
    /// Maximum number of nodes allowed before timeout.
    /// </summary>
    public int MaxNodesAllowed { get; set; }

    /// <summary>
    /// Time taken for the search in milliseconds.
    /// </summary>
    public double ElapsedMilliseconds { get; set; }

    /// <summary>
    /// Whether the search was terminated due to hitting the node expansion limit.
    /// </summary>
    public bool TimedOut => NodesExpanded >= MaxNodesAllowed;

    /// <summary>
    /// Whether a valid path was found.
    /// </summary>
    public bool PathFound { get; set; }

    /// <summary>
    /// Start position in grid coordinates.
    /// </summary>
    public (int X, int Y) StartGrid { get; set; }

    /// <summary>
    /// End position in grid coordinates.
    /// </summary>
    public (int X, int Y) EndGrid { get; set; }

    /// <summary>
    /// Start direction for the search.
    /// </summary>
    public GridDirection StartDirection { get; set; }

    /// <summary>
    /// End direction for the search.
    /// </summary>
    public GridDirection EndDirection { get; set; }
}
