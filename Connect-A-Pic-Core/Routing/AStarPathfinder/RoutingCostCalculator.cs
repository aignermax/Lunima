namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Calculates movement costs and heuristics for A* pathfinding.
/// Encapsulates the cost model for waveguide routing.
/// </summary>
public class RoutingCostCalculator
{
    /// <summary>
    /// Cost per micrometer of straight travel.
    /// </summary>
    public double StraightCostPerMicrometer { get; set; } = 1.0;

    /// <summary>
    /// Cost penalty per 90-degree turn.
    /// Higher values prefer straighter paths with fewer bends.
    /// Default 50 means a 90° turn costs as much as 50µm of straight travel.
    /// </summary>
    public double TurnCostPer90Degrees { get; set; } = 50.0;

    /// <summary>
    /// Minimum bend radius in micrometers.
    /// Used to determine minimum straight run before turns.
    /// </summary>
    public double MinBendRadiusMicrometers { get; set; } = 10.0;

    /// <summary>
    /// Minimum number of cells to travel straight before allowing a turn.
    /// This ensures there's enough space for a proper bend.
    /// Typically 2x bend radius / cell size.
    /// </summary>
    public int MinStraightRunCells { get; set; } = 20;

    /// <summary>
    /// Grid cell size for cost calculations.
    /// </summary>
    public double CellSizeMicrometers { get; set; } = 1.0;

    /// <summary>
    /// Calculates the cost to move from one node to an adjacent cell.
    /// </summary>
    /// <param name="from">Current node</param>
    /// <param name="toX">Target X coordinate</param>
    /// <param name="toY">Target Y coordinate</param>
    /// <param name="toDirection">Direction of movement to target</param>
    /// <returns>Movement cost</returns>
    public double CalculateMoveCost(AStarNode from, int toX, int toY, GridDirection toDirection)
    {
        // Base movement cost (distance)
        double cost = CellSizeMicrometers * StraightCostPerMicrometer;

        // Turn cost
        if (from.Direction != GridDirection.None && from.Direction != toDirection)
        {
            double turnAngle = Math.Abs(GridDirectionExtensions.GetTurnAngle(from.Direction, toDirection));
            cost += (turnAngle / 90.0) * TurnCostPer90Degrees;
        }

        return cost;
    }

    /// <summary>
    /// Checks if a turn is valid (respects minimum straight run).
    /// </summary>
    /// <param name="from">Current node</param>
    /// <param name="toDirection">Proposed direction</param>
    /// <returns>True if the turn is allowed</returns>
    public bool IsTurnValid(AStarNode from, GridDirection toDirection)
    {
        // First move or same direction is always valid
        if (from.Direction == GridDirection.None || from.Direction == toDirection)
        {
            return true;
        }

        // Check minimum straight run before turning
        return from.StraightRunLength >= MinStraightRunCells;
    }

    /// <summary>
    /// Calculates heuristic cost from current position to goal.
    /// Uses Manhattan distance (appropriate for 4-direction movement).
    /// </summary>
    public double CalculateHeuristic(int fromX, int fromY, GridDirection fromDir,
                                      int toX, int toY, GridDirection toDir)
    {
        int dx = Math.Abs(toX - fromX);
        int dy = Math.Abs(toY - fromY);

        // Manhattan distance
        double distance = (dx + dy) * CellSizeMicrometers;

        // Estimate turns needed
        double turnEstimate = 0;

        // If we need to move in both X and Y, we need at least one turn
        if (dx > 0 && dy > 0)
        {
            turnEstimate += TurnCostPer90Degrees * 0.5; // Partial weight to keep heuristic admissible
        }

        // If final direction doesn't match current direction, we may need another turn
        if (fromDir != GridDirection.None && fromDir != toDir)
        {
            double angleDiff = Math.Abs(GridDirectionExtensions.GetTurnAngle(fromDir, toDir));
            if (angleDiff > 0)
            {
                turnEstimate += (angleDiff / 90.0) * TurnCostPer90Degrees * 0.3;
            }
        }

        return distance * StraightCostPerMicrometer + turnEstimate;
    }

    /// <summary>
    /// Creates a cost calculator with settings derived from routing parameters.
    /// </summary>
    public static RoutingCostCalculator FromRoutingParameters(
        double cellSize, double minBendRadius, double minSpacing)
    {
        return new RoutingCostCalculator
        {
            CellSizeMicrometers = cellSize,
            MinBendRadiusMicrometers = minBendRadius,
            MinStraightRunCells = (int)Math.Ceiling(minBendRadius * 2 / cellSize)
        };
    }
}
