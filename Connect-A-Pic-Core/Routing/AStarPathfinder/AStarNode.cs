namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Node for A* pathfinding including direction state.
/// Direction is tracked because turning has a cost in waveguide routing.
/// </summary>
public class AStarNode : IComparable<AStarNode>
{
    /// <summary>
    /// X position in grid cells.
    /// </summary>
    public int X { get; }

    /// <summary>
    /// Y position in grid cells.
    /// </summary>
    public int Y { get; }

    /// <summary>
    /// Current direction of travel.
    /// </summary>
    public GridDirection Direction { get; }

    /// <summary>
    /// Cost from start to this node (actual cost).
    /// </summary>
    public double GCost { get; set; }

    /// <summary>
    /// Heuristic cost from this node to goal (estimated cost).
    /// </summary>
    public double HCost { get; set; }

    /// <summary>
    /// Total cost (G + H). Used for priority queue ordering.
    /// </summary>
    public double FCost => GCost + HCost;

    /// <summary>
    /// Parent node for path reconstruction.
    /// </summary>
    public AStarNode? Parent { get; set; }

    /// <summary>
    /// Number of consecutive cells traveled in current direction.
    /// Used to enforce minimum straight lengths before allowing turns.
    /// </summary>
    public int StraightRunLength { get; set; }

    public AStarNode(int x, int y, GridDirection direction)
    {
        X = x;
        Y = y;
        Direction = direction;
        GCost = double.MaxValue;
        HCost = 0;
        StraightRunLength = 0;
    }

    public int CompareTo(AStarNode? other)
    {
        if (other == null) return -1;
        int compare = FCost.CompareTo(other.FCost);
        if (compare == 0)
        {
            // Tie-breaker: prefer nodes closer to goal (lower H)
            compare = HCost.CompareTo(other.HCost);
        }
        return compare;
    }

    /// <summary>
    /// Gets a unique key for this node state (position + direction).
    /// Two nodes at the same position but different directions are different states.
    /// </summary>
    public (int x, int y, GridDirection dir) GetKey() => (X, Y, Direction);

    public override bool Equals(object? obj)
    {
        return obj is AStarNode other && X == other.X && Y == other.Y && Direction == other.Direction;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y, Direction);
    }

    public override string ToString()
    {
        return $"({X}, {Y}) {Direction} G={GCost:F1} H={HCost:F1} F={FCost:F1}";
    }
}
