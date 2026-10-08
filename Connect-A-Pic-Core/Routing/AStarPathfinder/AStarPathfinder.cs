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
    /// Increased to 200000 to handle complex layouts with many obstacles.
    /// </summary>
    public int MaxNodesExpanded { get; set; } = 200000;

    /// <summary>Default <see cref="GoalTolerance"/> in grid cells.</summary>
    public const int DefaultGoalTolerance = 3;

    /// <summary>
    /// Distance tolerance for reaching the goal (in grid cells).
    /// With 5µm cells, 3 cells = 15µm tolerance.
    /// </summary>
    public int GoalTolerance { get; set; } = DefaultGoalTolerance;

    /// <summary>
    /// When true, the goal also accepts arrivals laterally offset from the
    /// pin's entry axis (within GoalTolerance); the path smoother then snaps
    /// the final approach onto the axis. Off by default because an exact
    /// on-axis arrival smooths more reliably. Enabled as a retry when the
    /// strict search fails, e.g. because another waveguide crosses the entry
    /// axis — without the retry such pins are unreachable and burn the whole
    /// node budget before falling back to a blocked route.
    /// </summary>
    public bool AllowLateralGoalTolerance { get; set; }

    /// <summary>
    /// When true (default), the search expands all 8 directions including 45°
    /// diagonals (octile routing). When false, only the 4 cardinal directions
    /// are expanded — a much smaller search space for faster everyday routing.
    /// </summary>
    public bool UseDiagonals { get; set; } = true;

    /// <summary>
    /// Node count at which <see cref="OnEscalationThresholdReached"/> fires once
    /// (default: never). Lets a caller keep a single continuous search while still
    /// surfacing "this route is complex" at the point a separate quick phase would
    /// have given up.
    /// </summary>
    public int EscalationThresholdNodes { get; set; } = int.MaxValue;

    /// <summary>Fired once when the search expands its <see cref="EscalationThresholdNodes"/>-th node.</summary>
    public Action? OnEscalationThresholdReached { get; set; }

    /// <summary>
    /// When set, the search may cross other waveguides where a crossing component fits
    /// (see <see cref="CrossingInsertion.CrossingStep"/>); null (default) keeps the classic
    /// avoid-only search.
    /// </summary>
    public CrossingInsertion.CrossingStep? Crossings { get; set; }

    /// <summary>
    /// True when the last <see cref="FindPath"/> returned null because the open set emptied
    /// — a proof that no goal-reaching path exists in the reachable region — rather than
    /// because the node budget cut the search short or cancellation stopped it. Lets a
    /// caller skip follow-up searches that cannot succeed.
    /// </summary>
    public bool LastSearchProvedNoPath { get; private set; }

    /// <summary>
    /// True when the last <see cref="FindPath"/> dequeued at least one node that would
    /// satisfy the goal test with <see cref="AllowLateralGoalTolerance"/> enabled. When
    /// <see cref="LastSearchProvedNoPath"/> is also true, every reachable state was
    /// expanded, so a lateral-tolerance retry cannot succeed either and may be skipped.
    /// </summary>
    public bool LastSearchReachedGoalVicinity { get; private set; }

    /// <summary>
    /// Node expansions the last <see cref="FindPath"/> performed. Diagnostic surface:
    /// lets callers measure what a coarse retry actually costs per
    /// blocked wire, without re-running the search.
    /// </summary>
    public int LastSearchNodesExpanded { get; private set; }

    public AStarPathfinder(PathfindingGrid grid, RoutingCostCalculator costCalculator)
    {
        _grid = grid;
        _costCalculator = costCalculator;
    }

    /// <summary>
    /// How often (in node expansions) to check the cancellation token.
    /// Lower values = more responsive cancellation, slight overhead per check.
    /// </summary>
    private const int CancellationCheckInterval = 500;

    /// <summary>
    /// Maximum allowed direction change per step in degrees.
    /// Turns sharper than 90° cannot be built as a single fabricable bend.
    /// </summary>
    private const double MaxTurnAngleDegrees = 90.0;

    /// <summary>
    /// Finds a path from start to end, respecting pin directions.
    /// </summary>
    /// <param name="startX">Start position X in grid cells</param>
    /// <param name="startY">Start position Y in grid cells</param>
    /// <param name="startDirection">Required initial direction (from pin angle)</param>
    /// <param name="endX">End position X in grid cells</param>
    /// <param name="endY">End position Y in grid cells</param>
    /// <param name="endDirection">Required final direction (direction to enter end pin)</param>
    /// <param name="cancellationToken">Token to cancel the search (checked every 500 nodes).</param>
    /// <returns>List of nodes forming the path, or null if no path found or cancelled</returns>
    public List<AStarNode>? FindPath(int startX, int startY, GridDirection startDirection,
                                      int endX, int endY, GridDirection endDirection,
                                      CancellationToken cancellationToken = default)
    {
        LastSearchProvedNoPath = false;
        LastSearchReachedGoalVicinity = false;
        LastSearchNodesExpanded = 0;
        var openSet = new PriorityQueue<AStarNode, double>(initialCapacity: 4096);
        var visited = new Dictionary<long, AStarNode>(capacity: 4096);
        var neighborBuffer = new List<AStarNode>(8);

        // Create start node.
        // A pin start needs room for only ONE arc tangent before the first turn (the
        // upcoming bend), while interior turns need two — the previous and the next arc —
        // which is what MinStraightRunCells (≈ 2 × bend radius) encodes. Granting the start
        // node half that run makes the first turn legal one tangent from the pin, so the
        // first bend can begin directly at the pin (pin-lead-stub field finding). The -1
        // keeps the first apex strictly beyond the tangent, so a short pin-side straight
        // survives smoothing for the in-canvas shift handles.
        var startNode = new AStarNode(startX, startY, startDirection)
        {
            GCost = 0,
            StraightRunLength = Math.Max(0, (_costCalculator.MinStraightRunCells - 1) / 2)
        };
        startNode.HCost = _costCalculator.CalculateHeuristic(
            startX, startY, startDirection, endX, endY, endDirection);

        openSet.Enqueue(startNode, startNode.FCost);
        visited[StateKey(startNode)] = startNode;

        int nodesExpanded = 0;

        while (openSet.Count > 0 && nodesExpanded < MaxNodesExpanded)
        {
            // Check cancellation periodically to remain responsive
            if (nodesExpanded % CancellationCheckInterval == 0 && cancellationToken.IsCancellationRequested)
            {
                LastSearchProvedNoPath = false;
                LastSearchNodesExpanded = nodesExpanded;
                return null;
            }

            var current = openSet.Dequeue();
            nodesExpanded++;
            if (nodesExpanded == EscalationThresholdNodes)
                OnEscalationThresholdReached?.Invoke();

            // Check if we reached the goal
            if (IsGoalReached(current, endX, endY, endDirection))
            {
                LastSearchReachedGoalVicinity = true;
                var path = ReconstructPath(current);

                // A waveguide cannot cross itself (no optical model for that): discard
                // looping arrivals — e.g. a full 360° circle at the start pin — and keep
                // searching for a loop-free alternative.
                if (!PathLoopDetector.IsSelfIntersecting(path))
                {
                    LastSearchNodesExpanded = nodesExpanded;
                    return path;
                }

                // Forget this looping arrival's grid state, otherwise its (cheaper) entry
                // stays in the visited map and rejects a later, more expensive but loop-free
                // arrival at the same state key — making the search report "no path" even
                // though one exists. Only drop the entry if it is still this very node.
                var loopingKey = StateKey(current);
                if (visited.TryGetValue(loopingKey, out var stored) && ReferenceEquals(stored, current))
                {
                    visited.Remove(loopingKey);
                }
                continue;
            }
            if (!AllowLateralGoalTolerance && !LastSearchReachedGoalVicinity
                && IsGoalReached(current, endX, endY, endDirection, GoalTolerance))
            {
                LastSearchReachedGoalVicinity = true;
            }

            // Expand neighbors
            CollectNeighbors(current, endX, endY, endDirection, visited, neighborBuffer);
            foreach (var neighbor in neighborBuffer)
            {
                visited[StateKey(neighbor)] = neighbor;
                openSet.Enqueue(neighbor, neighbor.FCost);
            }
        }

        // No path found: hitting the budget with frontier left is a cut-short
        // search; an empty open set is a proof that no path exists.
        LastSearchProvedNoPath = openSet.Count == 0;
        LastSearchNodesExpanded = nodesExpanded;
        return null;
    }

    /// <summary>
    /// State identity of a node in the octile search, packed into one long for cheap
    /// hashing. The straight-run length is part of the state: a cheap arrival with a
    /// short run must not shadow a costlier arrival with a long run, because only the
    /// latter may be allowed to turn (IsTurnValid). Runs are capped at the largest
    /// value IsTurnValid ever requires. Bit budget: X 28, Y 20, direction+1 4, run 12.
    /// </summary>
    private long StateKey(AStarNode n) =>
        StateKey(n.X, n.Y, n.Direction, n.StraightRunLength);

    private long StateKey(int x, int y, GridDirection dir, int straightRunLength) =>
        ((long)x << 36)
        | ((long)y << 16)
        | ((long)((int)dir + 1) << 12)
        | (uint)Math.Min(straightRunLength, _costCalculator.MinStraightRunCells);

    /// <summary>
    /// Checks if the current node has reached the goal.
    /// By default the node must be ON the pin's entry axis (zero perpendicular
    /// offset) — tolerance applies only along the approach direction, because
    /// an off-axis landing so close to the terminal smooths unreliably. With
    /// <see cref="AllowLateralGoalTolerance"/> small lateral offsets are also
    /// accepted and the path smoother snaps the approach onto the axis.
    /// </summary>
    private bool IsGoalReached(AStarNode node, int endX, int endY, GridDirection endDirection) =>
        IsGoalReached(node, endX, endY, endDirection, AllowLateralGoalTolerance ? GoalTolerance : 0);

    /// <summary>Goal test with a caller-chosen lateral tolerance (cells); see the overload.</summary>
    private bool IsGoalReached(AStarNode node, int endX, int endY, GridDirection endDirection, int maxCross)
    {
        if (node.Direction != endDirection)
            return false;

        int dx = endX - node.X;
        int dy = endY - node.Y;
        if (dx == 0 && dy == 0)
            return true;

        var (ux, uy) = endDirection.GetDelta();

        // Perpendicular offset from the entry axis: exactly zero in strict
        // mode, within GoalTolerance in the lateral-tolerance retry.
        int cross = dx * uy - dy * ux;
        if (Math.Abs(cross) > maxCross)
            return false;

        // Goal must lie ahead along the entry direction, within tolerance
        int along = dx * ux + dy * uy;
        if (along <= 0)
            return false;

        int cellsAhead = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return cellsAhead <= GoalTolerance;
    }

    /// <summary>
    /// Collects the valid neighboring nodes of the current position into
    /// <paramref name="buffer"/> (cleared first — a reusable buffer avoids an
    /// iterator allocation per expansion). Neighbors that cannot beat the stored
    /// arrival at their state are rejected BEFORE the node is allocated — the
    /// accepted sequence is unchanged.
    /// </summary>
    private void CollectNeighbors(AStarNode current,
                                  int goalX, int goalY, GridDirection goalDir,
                                  Dictionary<long, AStarNode> visited,
                                  List<AStarNode> buffer)
    {
        buffer.Clear();

        // Distance from start for pin escape enforcement: the LATEST arrival at the
        // current state (the dequeued node may have been superseded by a cheaper one).
        int distanceFromStart =
            visited.TryGetValue(StateKey(current), out var latestArrival)
                ? latestArrival.DistanceFromStart
                : current.DistanceFromStart;

        var directions = UseDiagonals
            ? GridDirectionExtensions.GetAllDirections()
            : GridDirectionExtensions.GetCardinalDirections();

        foreach (var dir in directions)
        {
            var (dx, dy) = dir.GetDelta();
            int newX = current.X + dx;
            int newY = current.Y + dy;

            // Check bounds and obstacles
            if (_grid.IsBlocked(newX, newY))
            {
                if (TryCreateCrossingNeighbor(current, dir, goalX, goalY, goalDir, distanceFromStart, visited) is { } jump)
                    buffer.Add(jump);
                continue;
            }

            // Diagonal block check: a diagonal step is only allowed when BOTH
            // orthogonal neighbor cells are free, so the waveguide cannot
            // cut through a component corner.
            if (dir.IsDiagonal() &&
                (_grid.IsBlocked(current.X + dx, current.Y) ||
                 _grid.IsBlocked(current.X, current.Y + dy)))
                continue;

            // CRITICAL: Force pin escape - must travel minimum distance in start direction
            // before allowing ANY turn. This ensures waveguides exit components cleanly.
            if (distanceFromStart < _costCalculator.MinPinEscapeCells)
            {
                // Only allow movement in the original start direction
                if (dir != current.Direction)
                    continue;
            }

            // CRITICAL: Force pin arrival - must approach goal in the correct direction
            // for the last N cells. This ensures clean arrival at the end pin.
            // `<=` deliberately holds the last corner ONE cell further from the pin than
            // the departure check above (distanceFromStart < N) holds the first corner:
            // relaxing it to `<` lets the smoothed arrival arc clip obstacles registered
            // right next to the goal corridor (frozen sibling paths). The resulting
            // asymmetry is bounded by a single grid cell — quantization noise, unlike the
            // old distance-scaled escape (pin-lead-stub field finding).
            int distanceToGoal = Math.Abs(newX - goalX) + Math.Abs(newY - goalY);
            if (distanceToGoal <= _costCalculator.MinPinEscapeCells)
            {
                // Only allow movement in the goal direction when near the end
                if (dir != goalDir)
                    continue;
            }

            // Check if turn is valid (minimum straight run)
            if (!_costCalculator.IsTurnValid(current, dir))
                continue;

            // Don't allow sharp turns: only ±45° and ±90° direction changes are
            // physically realizable bends. This also excludes 180° reversals.
            if (current.Direction != GridDirection.None &&
                Math.Abs(GridDirectionExtensions.GetTurnAngle(current.Direction, dir)) > MaxTurnAngleDegrees)
                continue;

            // Calculate costs (including proximity penalty for being near other waveguides
            // and pin reservation zones)
            double moveCost = _costCalculator.CalculateMoveCost(current, newX, newY, dir);
            double proximityCost = _costCalculator.CalculateProximityCost(_grid, newX, newY);
            double pinZoneCost = _costCalculator.CalculatePinZoneCost(_grid, newX, newY);
            double newGCost = current.GCost + moveCost + proximityCost + pinZoneCost;

            // Skip a worse arrival before paying for the node — the g-check the
            // search loop used to run after construction.
            int newStraightRun = (current.Direction == dir) ? current.StraightRunLength + 1 : 1;
            if (visited.TryGetValue(StateKey(newX, newY, dir, newStraightRun), out var existingNode)
                && newGCost >= existingNode.GCost)
                continue;

            double newHCost = _costCalculator.CalculateHeuristic(
                newX, newY, dir, goalX, goalY, goalDir);

            var neighbor = new AStarNode(newX, newY, dir)
            {
                GCost = newGCost,
                HCost = newHCost,
                Parent = current,
                StraightRunLength = newStraightRun,
                DistanceFromStart = distanceFromStart + 1
            };

            buffer.Add(neighbor);
        }
    }

    /// <summary>
    /// The neighbor reached by jumping straight across the waveguide that blocks the next
    /// cell, or null when crossings are off, the move is not a straight continuation, or no
    /// crossing fits there (see <see cref="CrossingInsertion.CrossingStep.TryJump"/>).
    /// </summary>
    private AStarNode? TryCreateCrossingNeighbor(AStarNode current, GridDirection dir,
                                                 int goalX, int goalY, GridDirection goalDir,
                                                 int distanceFromStart, Dictionary<long, AStarNode> visited)
    {
        if (Crossings == null || dir.IsDiagonal() || dir != current.Direction)
            return null;
        var (dx, dy) = dir.GetDelta();
        if (!Crossings.TryJump(current.X, current.Y, dx, dy, current.StraightRunLength,
                               out int span, out int runAfter, out var crossings))
            return null;

        int landX = current.X + dx * span, landY = current.Y + dy * span;
        int distanceToGoal = Math.Abs(landX - goalX) + Math.Abs(landY - goalY);
        if (distanceToGoal <= _costCalculator.MinPinEscapeCells && dir != goalDir)
            return null;

        double stepCost = _costCalculator.CalculateMoveCost(current, current.X + dx, current.Y + dy, dir);
        double gCost = current.GCost + stepCost * span + Crossings.PenaltyCost * crossings.Count;
        // The run restarts behind the last crossing; the landing already leaves room for a
        // turn's arc, so the next bend cannot reach back into a crossing.
        int straightRun = runAfter;
        if (visited.TryGetValue(StateKey(landX, landY, dir, straightRun), out var existing) && gCost >= existing.GCost)
            return null;

        return new AStarNode(landX, landY, dir)
        {
            GCost = gCost,
            HCost = _costCalculator.CalculateHeuristic(landX, landY, dir, goalX, goalY, goalDir),
            Parent = current,
            StraightRunLength = straightRun,
            DistanceFromStart = distanceFromStart + span,
            Crossings = crossings,
        };
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
