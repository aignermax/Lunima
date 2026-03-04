namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Manages the sector decomposition of the pathfinding grid for HPA*.
/// Detects portals on sector boundaries and precomputes intra-sector paths.
/// </summary>
public class SectorGraph
{
    /// <summary>
    /// Size of each sector in grid cells.
    /// </summary>
    public int SectorSizeCells { get; }

    /// <summary>
    /// Number of sector columns.
    /// </summary>
    public int SectorCols { get; }

    /// <summary>
    /// Number of sector rows.
    /// </summary>
    public int SectorRows { get; }

    private readonly PathfindingGrid _grid;
    private readonly RoutingCostCalculator _costCalculator;
    private readonly List<SectorPortal> _portals = new();
    private readonly Dictionary<int, List<SectorGraphEdge>> _adjacency = new();
    private int _nextPortalId;

    /// <summary>
    /// All portals in the graph.
    /// </summary>
    public IReadOnlyList<SectorPortal> Portals => _portals;

    /// <summary>
    /// All edges (adjacency list keyed by portal ID).
    /// </summary>
    public IReadOnlyDictionary<int, List<SectorGraphEdge>> Adjacency => _adjacency;

    public SectorGraph(
        PathfindingGrid grid,
        RoutingCostCalculator costCalculator,
        int sectorSizeCells = 50)
    {
        _grid = grid;
        _costCalculator = costCalculator;
        SectorSizeCells = sectorSizeCells;
        SectorCols = (grid.Width + sectorSizeCells - 1) / sectorSizeCells;
        SectorRows = (grid.Height + sectorSizeCells - 1) / sectorSizeCells;
    }

    /// <summary>
    /// Builds the complete sector graph: detects all portals and connects them.
    /// </summary>
    public void Build()
    {
        _portals.Clear();
        _adjacency.Clear();
        _nextPortalId = 0;

        DetectAllPortals();
        ConnectAdjacentPortals();
        ComputeIntraSectorEdges();
    }

    private void DetectAllPortals()
    {
        for (int col = 0; col < SectorCols; col++)
        {
            for (int row = 0; row < SectorRows; row++)
            {
                // Detect portals on east boundary (shared with col+1)
                if (col + 1 < SectorCols)
                {
                    DetectPortalsOnVerticalEdge(col, row, SectorEdge.East);
                }

                // Detect portals on north boundary (shared with row+1)
                if (row + 1 < SectorRows)
                {
                    DetectPortalsOnHorizontalEdge(col, row, SectorEdge.North);
                }
            }
        }
    }

    private void DetectPortalsOnVerticalEdge(int col, int row, SectorEdge edge)
    {
        int sectorStartY = row * SectorSizeCells;
        int sectorEndY = Math.Min((row + 1) * SectorSizeCells, _grid.Height);
        int boundaryX = (col + 1) * SectorSizeCells;

        if (boundaryX >= _grid.Width) return;

        int? runStart = null;

        for (int y = sectorStartY; y < sectorEndY; y++)
        {
            bool leftFree = _grid.GetCellState(boundaryX - 1, y) != 1;
            bool rightFree = _grid.GetCellState(boundaryX, y) != 1;

            if (leftFree && rightFree)
            {
                runStart ??= y - sectorStartY;
            }
            else
            {
                if (runStart.HasValue)
                {
                    CreatePortalPair(col, row, edge, runStart.Value,
                        y - sectorStartY - 1, boundaryX, sectorStartY);
                    runStart = null;
                }
            }
        }

        if (runStart.HasValue)
        {
            CreatePortalPair(col, row, edge, runStart.Value,
                sectorEndY - sectorStartY - 1, boundaryX, sectorStartY);
        }
    }

    private void DetectPortalsOnHorizontalEdge(int col, int row, SectorEdge edge)
    {
        int sectorStartX = col * SectorSizeCells;
        int sectorEndX = Math.Min((col + 1) * SectorSizeCells, _grid.Width);
        int boundaryY = (row + 1) * SectorSizeCells;

        if (boundaryY >= _grid.Height) return;

        int? runStart = null;

        for (int x = sectorStartX; x < sectorEndX; x++)
        {
            bool belowFree = _grid.GetCellState(x, boundaryY - 1) != 1;
            bool aboveFree = _grid.GetCellState(x, boundaryY) != 1;

            if (belowFree && aboveFree)
            {
                runStart ??= x - sectorStartX;
            }
            else
            {
                if (runStart.HasValue)
                {
                    CreatePortalPair(col, row, edge, runStart.Value,
                        x - sectorStartX - 1, boundaryY, sectorStartX);
                    runStart = null;
                }
            }
        }

        if (runStart.HasValue)
        {
            CreatePortalPair(col, row, edge, runStart.Value,
                sectorEndX - sectorStartX - 1, boundaryY, sectorStartX);
        }
    }

    private void CreatePortalPair(
        int col, int row, SectorEdge edge,
        int startOffset, int endOffset,
        int boundaryPos, int sectorOrigin)
    {
        int midOffset = (startOffset + endOffset) / 2;

        // Determine grid positions for the portal pair
        (int x1, int y1, int x2, int y2) = edge switch
        {
            SectorEdge.East => (
                boundaryPos - 1, sectorOrigin + midOffset,
                boundaryPos, sectorOrigin + midOffset),
            SectorEdge.North => (
                sectorOrigin + midOffset, boundaryPos - 1,
                sectorOrigin + midOffset, boundaryPos),
            _ => throw new ArgumentException($"Unexpected edge: {edge}")
        };

        var (adjCol, adjRow, adjEdge) = GetAdjacentSector(col, row, edge);

        var portalA = new SectorPortal
        {
            Id = _nextPortalId++,
            SectorCoords = (col, row),
            Edge = edge,
            GridPosition = (x1, y1),
            StartOffset = startOffset,
            EndOffset = endOffset
        };

        var portalB = new SectorPortal
        {
            Id = _nextPortalId++,
            SectorCoords = (adjCol, adjRow),
            Edge = adjEdge,
            GridPosition = (x2, y2),
            StartOffset = startOffset,
            EndOffset = endOffset
        };

        portalA.AdjacentPortal = portalB;
        portalB.AdjacentPortal = portalA;

        _portals.Add(portalA);
        _portals.Add(portalB);
    }

    private static (int col, int row, SectorEdge edge) GetAdjacentSector(
        int col, int row, SectorEdge edge)
    {
        return edge switch
        {
            SectorEdge.East => (col + 1, row, SectorEdge.West),
            SectorEdge.West => (col - 1, row, SectorEdge.East),
            SectorEdge.North => (col, row + 1, SectorEdge.South),
            SectorEdge.South => (col, row - 1, SectorEdge.North),
            _ => throw new ArgumentException($"Unknown edge: {edge}")
        };
    }

    private void ConnectAdjacentPortals()
    {
        foreach (var portal in _portals)
        {
            if (portal.AdjacentPortal == null) continue;
            // Only connect in one direction to avoid duplicates
            if (portal.Id > portal.AdjacentPortal.Id) continue;

            AddEdge(portal, portal.AdjacentPortal, cost: 1.0, isInterSector: true);
            AddEdge(portal.AdjacentPortal, portal, cost: 1.0, isInterSector: true);
        }
    }

    private void ComputeIntraSectorEdges()
    {
        // Group portals by sector
        var portalsBySector = _portals
            .GroupBy(p => p.SectorCoords)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (sectorCoords, sectorPortals) in portalsBySector)
        {
            if (sectorPortals.Count < 2) continue;

            // For each pair of portals in the same sector, compute local A* cost
            for (int i = 0; i < sectorPortals.Count; i++)
            {
                for (int j = i + 1; j < sectorPortals.Count; j++)
                {
                    var cost = ComputeLocalPathCost(
                        sectorCoords, sectorPortals[i], sectorPortals[j]);

                    if (cost < double.MaxValue)
                    {
                        AddEdge(sectorPortals[i], sectorPortals[j], cost, isInterSector: false);
                        AddEdge(sectorPortals[j], sectorPortals[i], cost, isInterSector: false);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Computes the path cost between two portals within the same sector
    /// using Manhattan distance as approximation (fast, avoids local A*).
    /// </summary>
    private double ComputeLocalPathCost(
        (int Col, int Row) sectorCoords,
        SectorPortal from, SectorPortal to)
    {
        var (x1, y1) = from.GridPosition;
        var (x2, y2) = to.GridPosition;

        // Check if direct path is blocked by scanning for obstacles
        int manhattan = Math.Abs(x2 - x1) + Math.Abs(y2 - y1);

        // Quick obstacle check along the L-shaped path
        if (IsLPathBlocked(x1, y1, x2, y2))
        {
            // Add penalty for obstacle detour
            return manhattan * _costCalculator.StraightCostPerMicrometer
                * _costCalculator.CellSizeMicrometers * 1.5;
        }

        return manhattan * _costCalculator.StraightCostPerMicrometer
            * _costCalculator.CellSizeMicrometers;
    }

    private bool IsLPathBlocked(int x1, int y1, int x2, int y2)
    {
        // Check horizontal then vertical
        int stepX = x2 > x1 ? 1 : -1;
        for (int x = x1; x != x2; x += stepX)
        {
            if (_grid.GetCellState(x, y1) == 1) return true;
        }
        int stepY = y2 > y1 ? 1 : -1;
        for (int y = y1; y != y2; y += stepY)
        {
            if (_grid.GetCellState(x2, y) == 1) return true;
        }
        return false;
    }

    private void AddEdge(SectorPortal from, SectorPortal to, double cost, bool isInterSector)
    {
        if (!_adjacency.ContainsKey(from.Id))
            _adjacency[from.Id] = new List<SectorGraphEdge>();

        _adjacency[from.Id].Add(new SectorGraphEdge
        {
            From = from,
            To = to,
            Cost = cost,
            IsInterSector = isInterSector
        });
    }

    /// <summary>
    /// Finds the nearest portals to a given grid position within the same sector.
    /// Returns portals sorted by distance (nearest first).
    /// </summary>
    public List<(SectorPortal Portal, double Cost)> FindNearestPortals(
        int gridX, int gridY, int maxPortals = 4)
    {
        int col = gridX / SectorSizeCells;
        int row = gridY / SectorSizeCells;
        col = Math.Clamp(col, 0, SectorCols - 1);
        row = Math.Clamp(row, 0, SectorRows - 1);

        return _portals
            .Where(p => p.SectorCoords == (col, row))
            .Select(p =>
            {
                var (px, py) = p.GridPosition;
                double dist = (Math.Abs(px - gridX) + Math.Abs(py - gridY))
                    * _costCalculator.StraightCostPerMicrometer
                    * _costCalculator.CellSizeMicrometers;
                return (Portal: p, Cost: dist);
            })
            .OrderBy(p => p.Cost)
            .Take(maxPortals)
            .ToList();
    }

    /// <summary>
    /// Gets the sector coordinates for a grid position.
    /// </summary>
    public (int Col, int Row) GetSectorForPosition(int gridX, int gridY)
    {
        int col = Math.Clamp(gridX / SectorSizeCells, 0, SectorCols - 1);
        int row = Math.Clamp(gridY / SectorSizeCells, 0, SectorRows - 1);
        return (col, row);
    }
}
