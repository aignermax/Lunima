using CAP_Core.Routing.PathSmoothing;
using CAP_Core.Routing.Grid;
using CAP_Core.Routing.Utilities;
using CAP_Core.Components;
using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Routing.GeometricSolvers;

namespace CAP_Core.Routing;

/// <summary>
/// Routes waveguides between physical pins, generating path segments.
/// Uses A* pathfinding with Manhattan fallback.
/// </summary>
public class WaveguideRouter
{
    /// <summary>
    /// Minimum bend radius in micrometers. Violating this causes high loss.
    /// </summary>
    public double MinBendRadiusMicrometers { get; set; } = 10.0;

    /// <summary>
    /// Allowed bend radii in micrometers (foundry-style discrete values).
    /// If empty, any radius >= MinBendRadiusMicrometers is allowed.
    /// When set, bends will snap to the smallest allowed radius that fits.
    /// </summary>
    public List<double> AllowedBendRadii { get; set; } = new() { 5, 10, 20, 50 };

    /// <summary>
    /// Minimum spacing between waveguides in micrometers.
    /// </summary>
    public double MinWaveguideSpacingMicrometers { get; set; } = 2.0;

    /// <summary>
    /// List of obstacles (component bounding boxes) to route around.
    /// </summary>
    public List<RoutingObstacle> Obstacles { get; } = new();

    /// <summary>
    /// The pathfinding grid for A* routing.
    /// </summary>
    public PathfindingGrid? PathfindingGrid { get; private set; }

    /// <summary>
    /// Cost calculator for A* routing.
    /// </summary>
    public RoutingCostCalculator CostCalculator { get; } = new();

    private TwoBendSolver? _twoBendSolver;

    /// <summary>
    /// Grid cell size in micrometers for A* pathfinding.
    /// Larger values = faster routing but less precise obstacle avoidance.
    /// Recommended: 3-5µm for most designs.
    /// </summary>
    public double AStarCellSize { get; set; } = 4.0;

    /// <summary>
    /// Clearance padding around components in micrometers.
    /// </summary>
    public double ObstaclePaddingMicrometers { get; set; } = 5.0;

    /// <summary>
    /// Initializes the pathfinding grid for A* routing.
    /// </summary>
    public void InitializePathfindingGrid(double minX, double minY, double maxX, double maxY,
                                           IEnumerable<Component> components,
                                           double? cellSize = null)
    {
        double size = cellSize ?? AStarCellSize;
        PathfindingGrid = new PathfindingGrid(minX, minY, maxX, maxY, size, ObstaclePaddingMicrometers);
        PathfindingGrid.RebuildFromComponents(components);

        CostCalculator.CellSizeMicrometers = size;
        CostCalculator.MinBendRadiusMicrometers = MinBendRadiusMicrometers;
        CostCalculator.MinStraightRunCells = (int)Math.Ceiling(MinBendRadiusMicrometers * 2 / size);
    }


    public void UpdateComponentObstacle(Component component) =>
        PathfindingGrid?.UpdateComponentObstacle(component);

    public void RemoveComponentObstacle(Component component) =>
        PathfindingGrid?.RemoveComponentObstacle(component);

    public void AddComponentObstacle(Component component) =>
        PathfindingGrid?.AddComponentObstacle(component);

    /// <summary>
    /// Routes a waveguide between two pins.
    /// Tries two-bend geometric solution first, then falls back to A* pathfinding.
    /// Returns invalid path if both fail (no Manhattan fallback for photonic routing).
    /// </summary>
    public RoutedPath Route(PhysicalPin startPin, PhysicalPin endPin)
    {
        Console.WriteLine($"[WaveguideRouter] Routing from {startPin.Name} to {endPin.Name}");

        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();
        double startAngle = startPin.GetAbsoluteAngle();
        double endAngle = endPin.GetAbsoluteAngle();

        double endInputAngle = AngleUtilities.NormalizeAngle(endAngle + 180);

        // Initialize geometric solver if needed
        _twoBendSolver ??= new TwoBendSolver(MinBendRadiusMicrometers, AllowedBendRadii, this);

        // Try geometric solution (straight line or two bends)
        var geometricPath = _twoBendSolver.TryTwoBendConnection(startPin, endPin);
        if (geometricPath != null)
        {
            Console.WriteLine("[WaveguideRouter] Geometric solver succeeded!");
            return geometricPath;
        }

        // Fall back to A* pathfinding
        Console.WriteLine("[WaveguideRouter] Two-bend failed, trying A*...");
        if (PathfindingGrid != null)
        {
            var astarPath = new RoutedPath();
            if (TryRouteAStar(startX, startY, startAngle, endX, endY, endInputAngle,
                              astarPath, startPin, endPin))
            {
                // Check if A* produced valid geometry (not just segments)
                Console.WriteLine($"[WaveguideRouter]   A* result: IsValid={astarPath.IsValid}, IsInvalidGeometry={astarPath.IsInvalidGeometry}, Segments={astarPath.Segments.Count}");
                if (astarPath.IsValid && !astarPath.IsInvalidGeometry && astarPath.Segments.Count > 0)
                {
                    Console.WriteLine("[WaveguideRouter] A* succeeded with valid geometry!");
                    return astarPath;
                }
                else
                {
                    Console.WriteLine($"[WaveguideRouter] A* produced invalid geometry (IsValid={astarPath.IsValid}, IsInvalidGeometry={astarPath.IsInvalidGeometry})");
                }
            }
            else
            {
                Console.WriteLine("[WaveguideRouter] A* pathfinding failed (no path found)");
            }
        }

        // All strategies failed - return invalid path
        Console.WriteLine("[WaveguideRouter] All routing strategies failed, returning invalid path");
        var invalidPath = new RoutedPath
        {
            IsInvalidGeometry = true
        };
        return invalidPath;
    }

    /// <summary>
    /// Checks if any segment in a path passes through blocked cells.
    /// </summary>
    public bool IsPathBlocked(IEnumerable<PathSegment> segments)
    {
        if (PathfindingGrid == null) return false;

        foreach (var segment in segments)
        {
            if (segment is StraightSegment)
            {
                if (IsLineBlocked(segment.StartPoint.X, segment.StartPoint.Y,
                                  segment.EndPoint.X, segment.EndPoint.Y))
                    return true;
            }
            else if (segment is BendSegment bend)
            {
                if (IsArcBlocked(bend)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Attempts to route using A* pathfinding with obstacle avoidance.
    /// </summary>
    private bool TryRouteAStar(double startX, double startY, double startAngle,
                                double endX, double endY, double endInputAngle,
                                RoutedPath path, PhysicalPin startPin, PhysicalPin endPin)
    {
        if (PathfindingGrid == null) return false;

        double corridorLength = MinBendRadiusMicrometers * 3;
        double corridorWidth = MinBendRadiusMicrometers;

        var clearedStart = PathfindingGrid.ClearPinCorridor(
            startX, startY, startAngle, corridorLength, corridorWidth);

        // Clear corridors in BOTH directions for the end pin:
        // 1. Facing direction (away from component) — ensures approach path is clear
        // 2. Input direction (into component) — ensures the terminal grid cell is reachable
        double endFacingAngle = AngleUtilities.NormalizeAngle(endInputAngle + 180);
        var clearedEndApproach = PathfindingGrid.ClearPinCorridor(
            endX, endY, endFacingAngle, corridorLength, corridorWidth);
        var clearedEndTerminal = PathfindingGrid.ClearPinCorridor(
            endX, endY, endInputAngle, corridorLength, corridorWidth);

        try
        {
            var (gridStartX, gridStartY) = PathfindingGrid.PhysicalToGrid(startX, startY);
            var (gridEndX, gridEndY) = PathfindingGrid.PhysicalToGrid(endX, endY);

            var startDir = GridDirectionExtensions.FromAngle(startAngle);
            var endDir = GridDirectionExtensions.FromAngle(endInputAngle);

            int originalEscapeCells = CostCalculator.MinPinEscapeCells;

            // Scale escape distance based on pin separation.
            // Both start escape + end approach must fit within the total distance,
            // with room left for turns. Use 1/6 of distance, minimum 2 cells.
            int gridDistance = Math.Abs(gridEndX - gridStartX) + Math.Abs(gridEndY - gridStartY);
            int scaledEscape = Math.Min(originalEscapeCells, Math.Max(2, gridDistance / 6));
            CostCalculator.MinPinEscapeCells = scaledEscape;

            // Also scale MinStraightRunCells for close pins to allow tighter turns
            int originalStraightRun = CostCalculator.MinStraightRunCells;
            int scaledStraightRun = Math.Min(originalStraightRun, Math.Max(2, gridDistance / 4));
            CostCalculator.MinStraightRunCells = scaledStraightRun;

            // Use flat A* with generous node limit for all routes
            var pathfinder = new AStarPathfinder.AStarPathfinder(PathfindingGrid, CostCalculator)
            {
                MaxNodesExpanded = 1_000_000  // 1M nodes - handles even very long routes
            };
            var gridPath = pathfinder.FindPath(gridStartX, gridStartY, startDir,
                                            gridEndX, gridEndY, endDir);

            // Loop detection: if path is >2× Manhattan distance, retry with minimal constraints
            if (gridPath != null && gridPath.Count > gridDistance * 2 && scaledEscape > 2)
            {
                CostCalculator.MinPinEscapeCells = 2;
                CostCalculator.MinStraightRunCells = 2;
                var retry = new AStarPathfinder.AStarPathfinder(PathfindingGrid, CostCalculator);
                var retryPath = retry.FindPath(gridStartX, gridStartY, startDir,
                                                gridEndX, gridEndY, endDir);
                if (retryPath != null && retryPath.Count < gridPath.Count)
                    gridPath = retryPath;
            }

            if (gridPath == null || gridPath.Count < 2)
            {
                CostCalculator.MinPinEscapeCells = 2;
                CostCalculator.MinStraightRunCells = 2;
                var fallback = new AStarPathfinder.AStarPathfinder(PathfindingGrid, CostCalculator);
                gridPath = fallback.FindPath(gridStartX, gridStartY, startDir,
                                              gridEndX, gridEndY, endDir);
            }

            CostCalculator.MinStraightRunCells = originalStraightRun;

            CostCalculator.MinPinEscapeCells = originalEscapeCells;

            if (gridPath == null || gridPath.Count < 2) return false;

            var smoother = new PathSmoother(PathfindingGrid, MinBendRadiusMicrometers, AllowedBendRadii);
            var smoothedPath = smoother.ConvertToSegments(gridPath, startPin, endPin);

            path.Segments.AddRange(smoothedPath.Segments);
            path.IsInvalidGeometry = smoothedPath.IsInvalidGeometry;
            path.DebugGridPath = gridPath;
            path.IsInvalidGeometry = smoothedPath.IsInvalidGeometry; // Propagate geometry validation flag

            // Success requires valid segments without geometry violations
            return path.Segments.Count > 0 && !path.IsInvalidGeometry;
        }
        finally
        {
            PathfindingGrid.RestoreCells(clearedStart);
            PathfindingGrid.RestoreCells(clearedEndApproach);
            PathfindingGrid.RestoreCells(clearedEndTerminal);
        }
    }

    /// <summary>
    /// Checks if a straight line passes through any blocked cells.
    /// </summary>
    private bool IsLineBlocked(double x1, double y1, double x2, double y2)
    {
        if (PathfindingGrid == null) return false;

        double dx = x2 - x1;
        double dy = y2 - y1;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.001) return false;

        dx /= length;
        dy /= length;

        double stepSize = PathfindingGrid.CellSizeMicrometers * 0.5;
        double margin = PathfindingGrid.CellSizeMicrometers;

        for (double t = margin; t < length - margin; t += stepSize)
        {
            double px = x1 + dx * t;
            double py = y1 + dy * t;
            var (gx, gy) = PathfindingGrid.PhysicalToGrid(px, py);
            if (PathfindingGrid.IsBlocked(gx, gy)) return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if an arc segment passes through blocked cells.
    /// </summary>
    private bool IsArcBlocked(BendSegment bend)
    {
        if (PathfindingGrid == null) return false;

        double startRad = bend.StartAngleDegrees * Math.PI / 180;
        double sweepRad = bend.SweepAngleDegrees * Math.PI / 180;
        double arcLength = Math.Abs(sweepRad) * bend.RadiusMicrometers;
        double stepLength = PathfindingGrid.CellSizeMicrometers * 0.5;
        int numSamples = Math.Max(10, (int)Math.Ceiling(arcLength / stepLength));

        double sign = Math.Sign(bend.SweepAngleDegrees);
        if (sign == 0) sign = 1;

        for (int i = 1; i < numSamples; i++)
        {
            double t = (double)i / numSamples;
            double angle = startRad + sweepRad * t;
            double px = bend.Center.X + bend.RadiusMicrometers * Math.Cos(angle - Math.PI / 2 * sign);
            double py = bend.Center.Y + bend.RadiusMicrometers * Math.Sin(angle - Math.PI / 2 * sign);

            var (gx, gy) = PathfindingGrid.PhysicalToGrid(px, py);
            if (PathfindingGrid.IsBlocked(gx, gy)) return true;
        }
        return false;
    }
}
