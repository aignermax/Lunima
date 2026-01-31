namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// A* pathfinder for waveguide routing with direction-aware node expansion.
/// Finds optimal paths while respecting turn costs and minimum straight run constraints.
/// </summary>
public class AStarPathfinder
{
    private readonly PathfindingGrid _grid;
    private readonly RoutingCostCalculator _costCalculator;

    /// <summary>
    /// Maximum nodes to expand before giving up (prevents infinite search on large grids).
    /// Lower values = faster but may miss longer paths.
    /// </summary>
    public int MaxNodesExpanded { get; set; } = 50000;

    /// <summary>
    /// Distance tolerance for reaching the goal (in grid cells).
    /// With 5µm cells, 3 cells = 15µm tolerance.
    /// </summary>
    public int GoalTolerance { get; set; } = 3;

    public AStarPathfinder(PathfindingGrid grid, RoutingCostCalculator costCalculator)
    {
        _grid = grid;
        _costCalculator = costCalculator;
    }

    /// <summary>
    /// Finds a path from start to end, respecting pin directions.
    /// </summary>
    /// <param name="startX">Start position X in grid cells</param>
    /// <param name="startY">Start position Y in grid cells</param>
    /// <param name="startDirection">Required initial direction (from pin angle)</param>
    /// <param name="endX">End position X in grid cells</param>
    /// <param name="endY">End position Y in grid cells</param>
    /// <param name="endDirection">Required final direction (direction to enter end pin)</param>
    /// <returns>List of nodes forming the path, or null if no path found</returns>
    public List<AStarNode>? FindPath(int startX, int startY, GridDirection startDirection,
                                      int endX, int endY, GridDirection endDirection)
    {
        var openSet = new PriorityQueue<AStarNode, double>();
        var visited = new Dictionary<(int, int, GridDirection), AStarNode>();

        // Create start node
        // StraightRunLength = 0 forces the path to go straight first before turning
        // This ensures waveguides exit components properly before bending
        var startNode = new AStarNode(startX, startY, startDirection)
        {
            GCost = 0,
            StraightRunLength = 0
        };
        startNode.HCost = _costCalculator.CalculateHeuristic(
            startX, startY, startDirection, endX, endY, endDirection);

        openSet.Enqueue(startNode, startNode.FCost);
        visited[startNode.GetKey()] = startNode;

        int nodesExpanded = 0;

        while (openSet.Count > 0 && nodesExpanded < MaxNodesExpanded)
        {
            var current = openSet.Dequeue();
            nodesExpanded++;

            // Check if we reached the goal
            if (IsGoalReached(current, endX, endY, endDirection))
            {
                return ReconstructPath(current);
            }

            // Expand neighbors
            foreach (var neighbor in GetNeighbors(current, endX, endY, endDirection))
            {
                var key = neighbor.GetKey();

                if (visited.TryGetValue(key, out var existingNode))
                {
                    // Skip if we've found a better path already
                    if (neighbor.GCost >= existingNode.GCost)
                        continue;
                }

                visited[key] = neighbor;
                openSet.Enqueue(neighbor, neighbor.FCost);
            }
        }

        // No path found
        return null;
    }

    /// <summary>
    /// Checks if the current node has reached the goal.
    /// </summary>
    private bool IsGoalReached(AStarNode node, int endX, int endY, GridDirection endDirection)
    {
        int distX = Math.Abs(node.X - endX);
        int distY = Math.Abs(node.Y - endY);

        // Must be within tolerance of goal
        if (distX > GoalTolerance || distY > GoalTolerance)
            return false;

        // At exact position, check direction
        if (distX == 0 && distY == 0)
        {
            return node.Direction == endDirection;
        }

        // Within tolerance - check if we're approaching from correct direction
        // and have enough straight run to reach the goal
        if (node.Direction == endDirection)
        {
            // Check if moving in our current direction gets us closer to goal
            var (dx, dy) = node.Direction.GetDelta();
            int newDistX = Math.Abs(node.X + dx - endX);
            int newDistY = Math.Abs(node.Y + dy - endY);

            // We're heading toward the goal in the right direction
            if (newDistX <= distX && newDistY <= distY)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets valid neighboring nodes from the current position.
    /// </summary>
    private IEnumerable<AStarNode> GetNeighbors(AStarNode current,
                                                  int goalX, int goalY, GridDirection goalDir)
    {
        foreach (var dir in GridDirectionExtensions.GetAllDirections())
        {
            var (dx, dy) = dir.GetDelta();
            int newX = current.X + dx;
            int newY = current.Y + dy;

            // Check bounds and obstacles
            if (_grid.IsBlocked(newX, newY))
                continue;

            // Check if turn is valid (minimum straight run)
            if (!_costCalculator.IsTurnValid(current, dir))
                continue;

            // Don't allow 180-degree turns (going backward)
            if (current.Direction != GridDirection.None && dir == current.Direction.GetOpposite())
                continue;

            // Calculate costs
            double moveCost = _costCalculator.CalculateMoveCost(current, newX, newY, dir);
            double newGCost = current.GCost + moveCost;
            double newHCost = _costCalculator.CalculateHeuristic(
                newX, newY, dir, goalX, goalY, goalDir);

            var neighbor = new AStarNode(newX, newY, dir)
            {
                GCost = newGCost,
                HCost = newHCost,
                Parent = current,
                StraightRunLength = (current.Direction == dir)
                    ? current.StraightRunLength + 1
                    : 1
            };

            yield return neighbor;
        }
    }

    /// <summary>
    /// Reconstructs the path from end node back to start.
    /// </summary>
    private List<AStarNode> ReconstructPath(AStarNode endNode)
    {
        var path = new List<AStarNode>();
        var current = endNode;

        while (current != null)
        {
            path.Add(current);
            current = current.Parent;
        }

        path.Reverse();
        return path;
    }
}
