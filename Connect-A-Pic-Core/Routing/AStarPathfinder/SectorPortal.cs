namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Edge of a sector boundary (N=top, S=bottom, E=right, W=left).
/// </summary>
public enum SectorEdge
{
    North,
    South,
    East,
    West
}

/// <summary>
/// A portal is a contiguous opening on a sector boundary that allows
/// movement between adjacent sectors. Used by HPA* for abstract pathfinding.
/// </summary>
public class SectorPortal
{
    /// <summary>
    /// Unique identifier for this portal.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Sector coordinates (column, row) of the sector this portal belongs to.
    /// </summary>
    public (int Col, int Row) SectorCoords { get; set; }

    /// <summary>
    /// Which edge of the sector this portal is on.
    /// </summary>
    public SectorEdge Edge { get; set; }

    /// <summary>
    /// Grid-space position of the portal center (representative cell).
    /// </summary>
    public (int X, int Y) GridPosition { get; set; }

    /// <summary>
    /// Start offset along the edge (in cells, relative to sector origin).
    /// </summary>
    public int StartOffset { get; set; }

    /// <summary>
    /// End offset along the edge (in cells, relative to sector origin).
    /// </summary>
    public int EndOffset { get; set; }

    /// <summary>
    /// The adjacent portal on the neighboring sector (symmetric pair).
    /// </summary>
    public SectorPortal? AdjacentPortal { get; set; }
}

/// <summary>
/// An edge in the abstract sector graph connecting two portals.
/// </summary>
public class SectorGraphEdge
{
    /// <summary>
    /// Source portal.
    /// </summary>
    public SectorPortal From { get; set; } = null!;

    /// <summary>
    /// Destination portal.
    /// </summary>
    public SectorPortal To { get; set; } = null!;

    /// <summary>
    /// Movement cost to traverse this edge (from local A* or zero for adjacent portals).
    /// </summary>
    public double Cost { get; set; }

    /// <summary>
    /// Whether this is an inter-sector edge (between adjacent portals on shared boundary)
    /// or an intra-sector edge (between two portals within the same sector).
    /// </summary>
    public bool IsInterSector { get; set; }
}
