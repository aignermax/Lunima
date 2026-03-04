namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// HPA* (Hierarchical Pathfinding A*) implementation.
/// For short routes: delegates to flat A*.
/// For long routes: uses abstract graph (portals) for fast pathfinding,
/// then refines through individual sectors.
/// </summary>
public class HierarchicalPathfinder
{
    private readonly PathfindingGrid _grid;
    private readonly RoutingCostCalculator _costCalculator;
    private readonly AStarPathfinder _flatPathfinder;
    private SectorGraph? _sectorGraph;
    private DistanceTransform? _distanceTransform;

    /// <summary>
    /// Manhattan distance threshold (in cells) below which flat A* is used.
    /// </summary>
    public int FlatSearchThreshold { get; set; } = 200;

    /// <summary>
    /// Whether the hierarchical graph has been built.
    /// </summary>
    public bool IsBuilt => _sectorGraph != null;

    /// <summary>
    /// The sector graph (exposed for testing).
    /// </summary>
    public SectorGraph? SectorGraph => _sectorGraph;

    /// <summary>
    /// The distance transform (exposed for testing and cost calculator).
    /// </summary>
    public DistanceTransform? DistanceTransform => _distanceTransform;

    public HierarchicalPathfinder(
        PathfindingGrid grid,
        RoutingCostCalculator costCalculator)
    {
        _grid = grid;
        _costCalculator = costCalculator;
        _flatPathfinder = new AStarPathfinder(grid, costCalculator);
    }

    /// <summary>
    /// Builds the sector graph and distance transform.
    /// Call once after the grid is initialized, then incrementally update.
    /// </summary>
    public void BuildSectorGraph(int sectorSizeCells = 50)
    {
        _sectorGraph = new SectorGraph(_grid, _costCalculator, sectorSizeCells);
        _sectorGraph.Build();

        _distanceTransform = new DistanceTransform(
            _grid.Width, _grid.Height,
            _costCalculator.CellSizeMicrometers,
            _costCalculator.MinSafeSpacingMicrometers);
        _distanceTransform.BuildFromGrid(_grid);
    }

    /// <summary>
    /// Finds a path from start to end using the appropriate strategy.
    /// Short distances: flat A*. Long distances: HPA* with sector graph.
    /// Returns List of AStarNode (compatible with PathSmoother).
    /// </summary>
    public List<AStarNode>? FindPath(
        int startX, int startY, GridDirection startDir,
        int endX, int endY, GridDirection endDir)
    {
        int manhattan = Math.Abs(endX - startX) + Math.Abs(endY - startY);

        // Short routes or no sector graph: use flat A*
        if (_sectorGraph == null || manhattan < FlatSearchThreshold)
        {
            return _flatPathfinder.FindPath(
                startX, startY, startDir,
                endX, endY, endDir);
        }

        // Long routes: use hierarchical search
        return HierarchicalSearch(
            startX, startY, startDir,
            endX, endY, endDir);
    }

    private List<AStarNode>? HierarchicalSearch(
        int startX, int startY, GridDirection startDir,
        int endX, int endY, GridDirection endDir)
    {
        var startPortals = _sectorGraph!.FindNearestPortals(startX, startY);
        var endPortals = _sectorGraph.FindNearestPortals(endX, endY);

        if (startPortals.Count == 0 || endPortals.Count == 0)
        {
            return _flatPathfinder.FindPath(
                startX, startY, startDir, endX, endY, endDir);
        }

        // Same sector — flat A*
        var startSector = _sectorGraph.GetSectorForPosition(startX, startY);
        var endSector = _sectorGraph.GetSectorForPosition(endX, endY);
        if (startSector == endSector)
        {
            return _flatPathfinder.FindPath(
                startX, startY, startDir, endX, endY, endDir);
        }

        // Try portal combinations
        List<AStarNode>? bestPath = null;
        double bestCost = double.MaxValue;

        foreach (var (startPortal, startCost) in startPortals)
        {
            foreach (var (endPortal, endCost) in endPortals)
            {
                var abstractPath = AbstractAStarSearch(startPortal, endPortal);
                if (abstractPath == null) continue;

                double abstractCost = startCost + endCost;
                foreach (var edge in abstractPath)
                    abstractCost += edge.Cost;

                if (abstractCost >= bestCost) continue;

                var refinedPath = RefinePath(
                    startX, startY, startDir,
                    endX, endY, endDir,
                    startPortal, endPortal, abstractPath);

                if (refinedPath != null)
                {
                    bestPath = refinedPath;
                    bestCost = abstractCost;
                }
            }
        }

        return bestPath ?? _flatPathfinder.FindPath(
            startX, startY, startDir, endX, endY, endDir);
    }

    private List<SectorGraphEdge>? AbstractAStarSearch(
        SectorPortal start, SectorPortal end)
    {
        var openSet = new PriorityQueue<int, double>();
        var cameFrom = new Dictionary<int, (int PortalId, SectorGraphEdge Edge)>();
        var gScore = new Dictionary<int, double>();
        var closedSet = new HashSet<int>();

        gScore[start.Id] = 0;
        openSet.Enqueue(start.Id, Heuristic(start, end));

        while (openSet.Count > 0)
        {
            int currentId = openSet.Dequeue();

            if (currentId == end.Id)
                return ReconstructAbstractPath(cameFrom, currentId, start.Id);

            if (!closedSet.Add(currentId)) continue;

            if (!_sectorGraph!.Adjacency.ContainsKey(currentId)) continue;

            foreach (var edge in _sectorGraph.Adjacency[currentId])
            {
                int neighborId = edge.To.Id;
                if (closedSet.Contains(neighborId)) continue;

                double tentativeG = gScore[currentId] + edge.Cost;

                if (!gScore.ContainsKey(neighborId) ||
                    tentativeG < gScore[neighborId])
                {
                    gScore[neighborId] = tentativeG;
                    cameFrom[neighborId] = (currentId, edge);
                    double f = tentativeG + Heuristic(edge.To, end);
                    openSet.Enqueue(neighborId, f);
                }
            }
        }

        return null;
    }

    private double Heuristic(SectorPortal from, SectorPortal to)
    {
        var (x1, y1) = from.GridPosition;
        var (x2, y2) = to.GridPosition;
        return (Math.Abs(x2 - x1) + Math.Abs(y2 - y1))
            * _costCalculator.StraightCostPerMicrometer
            * _costCalculator.CellSizeMicrometers;
    }

    private static List<SectorGraphEdge> ReconstructAbstractPath(
        Dictionary<int, (int PortalId, SectorGraphEdge Edge)> cameFrom,
        int endId, int startId)
    {
        var edges = new List<SectorGraphEdge>();
        int current = endId;

        while (current != startId && cameFrom.ContainsKey(current))
        {
            var (prevId, edge) = cameFrom[current];
            edges.Add(edge);
            current = prevId;
        }

        edges.Reverse();
        return edges;
    }

    /// <summary>
    /// Refines an abstract path into a full grid path by running local A*
    /// through each sector along the route.
    /// </summary>
    private List<AStarNode>? RefinePath(
        int startX, int startY, GridDirection startDir,
        int endX, int endY, GridDirection endDir,
        SectorPortal startPortal, SectorPortal endPortal,
        List<SectorGraphEdge> abstractPath)
    {
        var fullPath = new List<AStarNode>();

        // Build waypoints: start → portals → end
        var waypoints = new List<(int X, int Y)> { (startX, startY) };

        waypoints.Add(startPortal.GridPosition);
        foreach (var edge in abstractPath)
            waypoints.Add(edge.To.GridPosition);
        waypoints.Add((endX, endY));

        // Route between consecutive waypoints using flat A*
        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            var (fromX, fromY) = waypoints[i];
            var (toX, toY) = waypoints[i + 1];

            var dir = (i == 0) ? startDir
                : InferDirection(fromX, fromY, toX, toY);
            var toDir = (i == waypoints.Count - 2) ? endDir
                : InferDirection(fromX, fromY, toX, toY);

            var segment = _flatPathfinder.FindPath(
                fromX, fromY, dir, toX, toY, toDir);

            if (segment == null) return null;

            // Append (skip first to avoid duplicates at waypoints)
            int startIdx = (i == 0) ? 0 : 1;
            for (int j = startIdx; j < segment.Count; j++)
                fullPath.Add(segment[j]);
        }

        return fullPath.Count > 0 ? fullPath : null;
    }

    private static GridDirection InferDirection(
        int fromX, int fromY, int toX, int toY)
    {
        int dx = toX - fromX;
        int dy = toY - fromY;

        if (Math.Abs(dx) > Math.Abs(dy))
            return dx > 0 ? GridDirection.East : GridDirection.West;
        return dy > 0 ? GridDirection.North : GridDirection.South;
    }
}
