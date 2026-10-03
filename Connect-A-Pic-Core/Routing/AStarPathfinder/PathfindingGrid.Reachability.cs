namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Cheap whole-grid reachability flood used to prove that no route to a goal can exist,
/// so the expensive A* searches that would burn their full node budgets discovering the
/// same can be skipped (split out to keep the grid partials below the file-size limit).
/// </summary>
public partial class PathfindingGrid
{
    /// <summary>
    /// Flood-fills the free cells from the start position and answers whether any cell
    /// within <paramref name="goalToleranceCells"/> (Chebyshev) of the goal is reachable.
    /// The flood uses the same movement model as the A* search — cardinal moves, diagonal
    /// moves when <paramref name="useDiagonals"/> is set, and the same corner-cutting rule
    /// (a diagonal step needs both orthogonal cells free) — but ignores direction state,
    /// turn spacing and pin-escape constraints. Its reachable set is therefore a superset
    /// of what any constrained A* search could ever reach: a <c>false</c> verdict proves
    /// every such search would return null, while <c>true</c> proves nothing. Runs in
    /// O(width × height) — milliseconds where the searches it gates burn whole node budgets.
    /// </summary>
    /// <param name="startX">Start cell X (the A* search never checks this cell's own blocked state, so the flood seeds it regardless).</param>
    /// <param name="startY">Start cell Y.</param>
    /// <param name="goalX">Goal cell X.</param>
    /// <param name="goalY">Goal cell Y.</param>
    /// <param name="goalToleranceCells">Chebyshev distance around the goal that counts as reached — a superset of the A* goal acceptance (on-axis or laterally offset within tolerance).</param>
    /// <param name="useDiagonals">Match the routed search's movement model.</param>
    public bool CanReachGoalDirected(int startX, int startY, int goalX, int goalY,
        int goalToleranceCells, bool useDiagonals)
    {
        if (!IsInBounds(startX, startY) || !IsInBounds(goalX, goalY))
            return false;

        if (WithinGoalTolerance(startX, startY, goalX, goalY, goalToleranceCells))
            return true;

        var directions = useDiagonals
            ? GridDirectionExtensions.GetAllDirections()
            : GridDirectionExtensions.GetCardinalDirections();

        var visited = new byte[Width * Height];
        var stack = new Stack<int>();
        stack.Push(startY * Width + startX);
        visited[startY * Width + startX] = 1;

        while (stack.Count > 0)
        {
            int index = stack.Pop();
            int x = index % Width;
            int y = index / Width;

            foreach (var dir in directions)
            {
                var (dx, dy) = dir.GetDelta();
                int nx = x + dx;
                int ny = y + dy;

                if (IsBlocked(nx, ny))
                    continue;

                // Same corner-cutting rule as the A* search: a diagonal step is only
                // allowed when both orthogonal neighbor cells are free.
                if (dir.IsDiagonal() && (IsBlocked(x + dx, y) || IsBlocked(x, y + dy)))
                    continue;

                if (WithinGoalTolerance(nx, ny, goalX, goalY, goalToleranceCells))
                    return true;

                int neighborIndex = ny * Width + nx;
                if (visited[neighborIndex] != 0)
                    continue;

                visited[neighborIndex] = 1;
                stack.Push(neighborIndex);
            }
        }

        return false;
    }

    private static bool WithinGoalTolerance(int x, int y, int goalX, int goalY, int toleranceCells) =>
        Math.Max(Math.Abs(x - goalX), Math.Abs(y - goalY)) <= toleranceCells;
}
