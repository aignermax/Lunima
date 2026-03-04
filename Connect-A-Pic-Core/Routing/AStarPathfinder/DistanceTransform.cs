namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Precomputed BFS distance grid for O(1) proximity cost lookups.
/// Replaces the O(N²) brute-force search in RoutingCostCalculator
/// where N is the search radius in cells.
/// </summary>
public class DistanceTransform
{
    private readonly float[,] _distances;
    private readonly int _width;
    private readonly int _height;
    private readonly double _cellSizeMicrometers;

    /// <summary>
    /// Maximum distance stored in the grid (in micrometers).
    /// Cells beyond this distance get MaxDistance.
    /// </summary>
    public double MaxDistanceMicrometers { get; }

    public int Width => _width;
    public int Height => _height;

    /// <summary>
    /// Creates a new distance transform grid.
    /// </summary>
    /// <param name="width">Grid width in cells.</param>
    /// <param name="height">Grid height in cells.</param>
    /// <param name="cellSizeMicrometers">Cell size for distance conversion.</param>
    /// <param name="maxDistanceMicrometers">Maximum distance to track.</param>
    public DistanceTransform(
        int width, int height,
        double cellSizeMicrometers,
        double maxDistanceMicrometers)
    {
        _width = width;
        _height = height;
        _cellSizeMicrometers = cellSizeMicrometers;
        MaxDistanceMicrometers = maxDistanceMicrometers;
        _distances = new float[width, height];

        // Initialize all cells to max distance
        float maxDist = (float)maxDistanceMicrometers;
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                _distances[x, y] = maxDist;
    }

    /// <summary>
    /// Builds the distance transform from a PathfindingGrid using BFS.
    /// Computes distance from each cell to the nearest waveguide obstacle (state=2).
    /// </summary>
    public void BuildFromGrid(PathfindingGrid grid)
    {
        int maxCells = (int)Math.Ceiling(MaxDistanceMicrometers / _cellSizeMicrometers);
        var queue = new Queue<(int X, int Y, int Dist)>();

        // Seed BFS with all waveguide obstacle cells
        for (int x = 0; x < _width && x < grid.Width; x++)
        {
            for (int y = 0; y < _height && y < grid.Height; y++)
            {
                if (grid.GetCellState(x, y) == 2) // Waveguide obstacle
                {
                    _distances[x, y] = 0;
                    queue.Enqueue((x, y, 0));
                }
            }
        }

        // BFS expansion using 4-connected neighbors (Manhattan distance)
        while (queue.Count > 0)
        {
            var (cx, cy, dist) = queue.Dequeue();
            int nextDist = dist + 1;

            if (nextDist > maxCells) continue;

            float nextDistMicrometers = (float)(nextDist * _cellSizeMicrometers);

            ExpandNeighbor(cx + 1, cy, nextDist, nextDistMicrometers, queue);
            ExpandNeighbor(cx - 1, cy, nextDist, nextDistMicrometers, queue);
            ExpandNeighbor(cx, cy + 1, nextDist, nextDistMicrometers, queue);
            ExpandNeighbor(cx, cy - 1, nextDist, nextDistMicrometers, queue);
        }
    }

    private void ExpandNeighbor(
        int x, int y, int dist, float distMicrometers,
        Queue<(int X, int Y, int Dist)> queue)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height) return;
        if (_distances[x, y] <= distMicrometers) return;

        _distances[x, y] = distMicrometers;
        queue.Enqueue((x, y, dist));
    }

    /// <summary>
    /// Gets the distance to the nearest waveguide obstacle in micrometers. O(1) lookup.
    /// Returns MaxDistanceMicrometers if out of bounds or no nearby obstacle.
    /// </summary>
    public double GetDistanceMicrometers(int gridX, int gridY)
    {
        if (gridX < 0 || gridX >= _width || gridY < 0 || gridY >= _height)
            return MaxDistanceMicrometers;

        return _distances[gridX, gridY];
    }

    /// <summary>
    /// Incrementally updates the distance transform when a waveguide is added.
    /// Runs BFS from the new waveguide cells outward.
    /// </summary>
    public void AddWaveguideCells(IEnumerable<(int X, int Y)> cells)
    {
        int maxCells = (int)Math.Ceiling(MaxDistanceMicrometers / _cellSizeMicrometers);
        var queue = new Queue<(int X, int Y, int Dist)>();

        foreach (var (x, y) in cells)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height) continue;
            _distances[x, y] = 0;
            queue.Enqueue((x, y, 0));
        }

        while (queue.Count > 0)
        {
            var (cx, cy, dist) = queue.Dequeue();
            int nextDist = dist + 1;
            if (nextDist > maxCells) continue;

            float nextDistMicrometers = (float)(nextDist * _cellSizeMicrometers);
            ExpandNeighbor(cx + 1, cy, nextDist, nextDistMicrometers, queue);
            ExpandNeighbor(cx - 1, cy, nextDist, nextDistMicrometers, queue);
            ExpandNeighbor(cx, cy + 1, nextDist, nextDistMicrometers, queue);
            ExpandNeighbor(cx, cy - 1, nextDist, nextDistMicrometers, queue);
        }
    }

    /// <summary>
    /// Fully rebuilds the distance transform from the current grid state.
    /// Call after waveguide removal since BFS cannot efficiently "un-propagate".
    /// </summary>
    public void Rebuild(PathfindingGrid grid)
    {
        float maxDist = (float)MaxDistanceMicrometers;
        for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
                _distances[x, y] = maxDist;

        BuildFromGrid(grid);
    }
}
