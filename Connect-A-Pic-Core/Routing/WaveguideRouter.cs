using CAP_Core.Components.Core;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing;

/// <summary>
/// Routes waveguides between physical pins, generating path segments.
/// Uses A* pathfinding with Manhattan fallback.
/// </summary>
public partial class WaveguideRouter
{
    /// <summary>
    /// The connection's minimum bend radius in micrometers. Violating this causes high loss.
    /// </summary>
    public double MinBendRadiusMicrometers { get; set; } = 10.0;

    /// <summary>
    /// Bend-radius floor (µm) imposed by the active fabrication process
    /// (<c>WaveguideBendRadiusResolver</c>). <see cref="Route"/> first attempts the larger of
    /// this floor and <see cref="MinBendRadiusMicrometers"/>; when no clean path exists at the
    /// floor it retries at the connection radius and marks the result with
    /// <see cref="RoutedPath.ViolatesProcessMinBendRadius"/> so the design checks surface the
    /// violation. 0 means no process constraint. This is the canvas-wide value —
    /// <see cref="ConnectionProcessFloorProvider"/> can override it per connection.
    /// </summary>
    public double ProcessMinBendRadiusMicrometers { get; set; }

    /// <summary>
    /// Bend-radius floor (µm) for ELECTRICAL (metal trace) connections, sourced from the
    /// process metal cross-section via <c>MetalRoutingSpecFactory</c> (issue #854). RF metal
    /// routing needs curved bends just like waveguides — often LARGER than the optical floor —
    /// so electrical pin pairs use this floor instead of
    /// <see cref="ProcessMinBendRadiusMicrometers"/>. Defaults to the RF fallback
    /// (3 × the default 10 µm trace width); 0 means no metal constraint.
    /// </summary>
    public double MetalProcessMinBendRadiusMicrometers { get; set; } =
        MetalRouting.MetalRoutingSpec.DefaultMinBendRadiusMicrometers;

    /// <summary>Tolerance (µm) below which two bend radii count as equal.</summary>
    private const double RadiusToleranceMicrometers = 1e-6;

    /// <summary>
    /// Pin-to-pin distance (µm) below which the two pins sit at the same point — a
    /// perfect abutment (gdsfactory-style touching cells, pins snapped together on the
    /// canvas). Matches the router's endpoint tolerance
    /// (<see cref="SameEndpointToleranceMicrometers"/> in the direct-route partial) and the
    /// import pipeline's degenerate-route threshold, so all three agree on what an abutment is.
    /// </summary>
    private const double AbutmentPinDistanceMicrometers = 1.0;

    /// <summary>
    /// Allowed bend radii in micrometers (foundry-style discrete values).
    /// If empty, any radius >= MinBendRadiusMicrometers is allowed.
    /// When set, smoothing snaps each bend to the smallest allowed radius that fits; a
    /// post-routing pass (<see cref="AStarPathfinder.BendRadiusUpsizer"/>) then grows optical
    /// bends to the LARGEST allowed radius the free space around them permits (larger radii
    /// mean lower bend loss), once every sibling route is final. Direct/S-bend-first styled
    /// routes snap their arcs to the largest allowed radius already at build time (issue #888),
    /// since their geometry is fixed and the post-pass excludes them.
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

    private HierarchicalPathfinder? _hierarchicalPathfinder;

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
    /// Whether to use hierarchical pathfinding (HPA*) for long-distance routes.
    /// When false, uses flat A* with increased node limit for all routes.
    /// Set to false if experiencing routing detours or loops.
    /// </summary>
    public bool UseHierarchicalPathfinding { get; set; } = false;

    /// <summary>
    /// Routing policy (issue #860): when true, <see cref="Route"/> first tries the DIRECT
    /// styled geometry (straight / arc-S / sine / cobra per pin geometry, built by
    /// <see cref="InterconnectRouting.DirectRouteFirstPolicy"/>) and only falls back to A*
    /// when obstacles actually block the styled path. A* is never the first choice — it is
    /// the obstacle-avoidance fallback. Disable to force grid routing for every connection.
    /// </summary>
    public bool PreferDirectStyledRoutes { get; set; } = true;

    /// <summary>
    /// Whether the A* search may use 45° diagonal moves (octile routing).
    /// Diagonals yield shorter, more compact routes but roughly double the
    /// per-cell state space, making every re-route noticeably more expensive.
    /// Library default is true; the application layer decides the product
    /// default (currently opt-in via the Routing settings page).
    /// </summary>
    public bool UseDiagonalRouting
    {
        get => _fineSearch.UseDiagonals;
        set => _fineSearch.UseDiagonals = value;
    }

    /// <summary>
    /// Maximum nodes for Phase 1 (quick) pathfinding.
    /// Phase 1 provides fast results for simple and medium-complexity routes.
    /// </summary>
    public int Phase1MaxNodes
    {
        get => _fineSearch.Phase1MaxNodes;
        set => _fineSearch.Phase1MaxNodes = value;
    }

    /// <summary>
    /// Maximum nodes for Phase 2 (extended) pathfinding.
    /// Phase 2 is triggered when Phase 1 fails, allowing longer search for complex routes.
    /// </summary>
    public int Phase2MaxNodes
    {
        get => _fineSearch.Phase2MaxNodes;
        set => _fineSearch.Phase2MaxNodes = value;
    }

    /// <summary>
    /// Invoked when routing escalates to Phase 2 (complex path search).
    /// Use this to show a "Computing complex path..." indicator in the UI.
    /// Called on the routing thread (not the UI thread).
    /// </summary>
    public Action? OnComplexRouteStarted
    {
        get => _fineSearch.OnComplexRouteStarted;
        set => _fineSearch.OnComplexRouteStarted = value;
    }

    private IReadOnlyList<(double X, double Y, double Width, double Height)>? _allowedRoutingRegions;

    /// <summary>
    /// Rectangles (µm) waveguides may run in — the dies of an assembly — or null for the whole
    /// grid. Kept across grid re-initializations and applied to the current grid at once.
    /// </summary>
    public IReadOnlyList<(double X, double Y, double Width, double Height)>? AllowedRoutingRegions
    {
        get => _allowedRoutingRegions;
        set
        {
            _allowedRoutingRegions = value;
            if (PathfindingGrid != null)
                PathfindingGrid.AllowedRegions = value;
        }
    }

    /// <summary>
    /// Initializes the pathfinding grid for A* routing.
    /// </summary>
    public void InitializePathfindingGrid(double minX, double minY, double maxX, double maxY,
                                           IEnumerable<Component> components,
                                           double? cellSize = null)
    {
        double size = cellSize ?? AStarCellSize;
        PathfindingGrid = new PathfindingGrid(minX, minY, maxX, maxY, size, ObstaclePaddingMicrometers)
        {
            AllowedRegions = _allowedRoutingRegions,
        };
        PathfindingGrid.RebuildFromComponents(components);

        CostCalculator.CellSizeMicrometers = size;
        CostCalculator.MinBendRadiusMicrometers = MinBendRadiusMicrometers;
        CostCalculator.MinStraightRunCells = (int)Math.Ceiling(MinBendRadiusMicrometers * 2 / size);

        CostCalculator.DistanceTransformGrid = null;
    }

    /// <summary>
    /// Clears the pathfinding grid and hierarchical pathfinder.
    /// Used for test cleanup to prevent shared state pollution.
    /// </summary>
    public void ClearPathfindingGrid()
    {
        PathfindingGrid = null;
        _hierarchicalPathfinder = null;
        CostCalculator.DistanceTransformGrid = null;
    }

    /// <summary>
    /// Builds the hierarchical pathfinding graph for fast long-distance routing.
    /// Only builds if UseHierarchicalPathfinding is enabled.
    /// </summary>
    public void BuildHierarchicalGraph(int sectorSizeCells = 50)
    {
        if (PathfindingGrid == null) return;

        if (!UseHierarchicalPathfinding)
        {
            // HPA* disabled - clear any existing hierarchical pathfinder
            _hierarchicalPathfinder = null;
            CostCalculator.DistanceTransformGrid = null;
            return;
        }

        _hierarchicalPathfinder = new HierarchicalPathfinder(PathfindingGrid, CostCalculator);
        _hierarchicalPathfinder.BuildSectorGraph(sectorSizeCells);
        CostCalculator.DistanceTransformGrid = _hierarchicalPathfinder.DistanceTransform;

        // Wire DT incremental updates via PathfindingGrid callbacks
        var dt = _hierarchicalPathfinder.DistanceTransform;
        var grid = PathfindingGrid;
        grid.OnWaveguideCellsAdded = cells => dt?.AddWaveguideCells(cells);
        grid.OnAllWaveguidesCleared = () => dt?.Rebuild(grid);
    }

    /// <summary>
    /// Rebuilds only the distance transform (after waveguide changes).
    /// </summary>
    public void RebuildDistanceTransform()
    {
        if (PathfindingGrid == null || _hierarchicalPathfinder?.DistanceTransform == null) return;
        _hierarchicalPathfinder.DistanceTransform.Rebuild(PathfindingGrid);
    }

    public void UpdateComponentObstacle(Component component) =>
        PathfindingGrid?.UpdateComponentObstacle(component);

    public void RemoveComponentObstacle(Component component) =>
        PathfindingGrid?.RemoveComponentObstacle(component);

    public void AddComponentObstacle(Component component) =>
        PathfindingGrid?.AddComponentObstacle(component);

    /// <summary>
    /// Routes a waveguide between two pins. With <see cref="PreferDirectStyledRoutes"/> (the
    /// default) the DIRECT styled geometry is tried first and A* only runs when obstacles
    /// actually block the styled path (issue #860).
    /// The first attempt honors the process bend-radius floor
    /// (<see cref="ResolveProcessFloorFor"/> — per-connection when
    /// <see cref="ConnectionProcessFloorProvider"/> is wired, else the canvas-wide
    /// <see cref="ProcessMinBendRadiusMicrometers"/>); when no clean path exists at the floor,
    /// it retries at the connection radius and marks the result with
    /// <see cref="RoutedPath.ViolatesProcessMinBendRadius"/>. Falls back to Manhattan routing
    /// if all A* attempts fail; a self-intersecting or blocked fallback at the floor radius
    /// is discarded in favor of the connection radius, and unresolvable results are marked
    /// <see cref="RoutedPath.IsBlockedFallback"/>.
    /// Pins closer together than <see cref="AbutmentPinDistanceMicrometers"/> short-circuit
    /// all of the above: they sit at the same point, so the route is a minimal butt joint —
    /// a valid, unflagged pin-to-pin straight.
    /// </summary>
    /// <param name="startPin">Source pin.</param>
    /// <param name="endPin">Target pin.</param>
    /// <param name="cancellationToken">Token to cancel Phase 2 (e.g. when grid changes).</param>
    public RoutedPath Route(PhysicalPin startPin, PhysicalPin endPin,
                             CancellationToken cancellationToken = default)
    {
        _coarseRetry.Reset();
        // Crossings belong to the route that planned them: a route found another way (direct,
        // coarse retry) must not inherit those of an earlier call.
        ResetPlannedCrossings();
        try
        {
            return RouteCore(startPin, endPin, cancellationToken);
        }
        finally
        {
            OnRouteCoarseExpansionsRecorded?.Invoke(LastRouteCoarseNodesExpanded);
        }
    }

    private RoutedPath RouteCore(PhysicalPin startPin, PhysicalPin endPin,
                                  CancellationToken cancellationToken)
    {
        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();

        double abutmentDx = endX - startX;
        double abutmentDy = endY - startY;
        if (abutmentDx * abutmentDx + abutmentDy * abutmentDy
            < AbutmentPinDistanceMicrometers * AbutmentPinDistanceMicrometers)
        {
            return BuildAbutmentRoute(startX, startY, endX, endY);
        }

        double connectionRadius = MinBendRadiusMicrometers;
        double processFloor = ResolveProcessFloorFor(startPin, endPin);

        var startPlan = ActiveBreakoutFor(startPin);
        var endPlan = ActiveBreakoutFor(endPin);
        if (startPlan != null || endPlan != null)
        {
            return RouteWithBreakouts(startPin, endPin, startPlan, endPlan,
                connectionRadius, processFloor, cancellationToken);
        }
        return RoutePinPair(startPin, endPin, connectionRadius, processFloor,
            PreferDirectStyledRoutes, cancellationToken);
    }

    /// <summary>
    /// The plain pin-to-pin routing cascade: direct styled route (when
    /// <paramref name="tryDirect"/>), A* at the process floor and at the connection radius,
    /// coarse-grid retry, and the Manhattan fallback.
    /// </summary>
    private RoutedPath RoutePinPair(PhysicalPin startPin, PhysicalPin endPin,
                                    double connectionRadius, double processFloor, bool tryDirect,
                                    CancellationToken cancellationToken)
    {
        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();
        double startAngle = startPin.GetAbsoluteAngle();
        double endInputAngle = AngleUtilities.NormalizeAngle(endPin.GetAbsoluteAngle() + 180);

        double effectiveRadius = Math.Max(connectionRadius, processFloor);
        bool floorRaisesRadius = effectiveRadius > connectionRadius + RadiusToleranceMicrometers;

        if (tryDirect)
        {
            var direct = TryRouteDirect(startPin, endPin, effectiveRadius);
            if (direct != null)
                return direct;
        }

        if (PathfindingGrid != null)
        {
            var astarPath = new RoutedPath();
            if (TryRouteAStar(effectiveRadius, startX, startY, startAngle, endX, endY, endInputAngle,
                              astarPath, startPin, endPin, cancellationToken)
                && IsCleanAStarResult(astarPath))
            {
                return astarPath;
            }

            // Controlled degradation: the process floor found no clean path — retry at the
            // connection radius and surface the violation instead of degenerate geometry.
            if (floorRaisesRadius)
            {
                astarPath = new RoutedPath();
                if (TryRouteAStar(connectionRadius, startX, startY, startAngle, endX, endY, endInputAngle,
                                  astarPath, startPin, endPin, cancellationToken)
                    && IsCleanAStarResult(astarPath))
                {
                    astarPath.ViolatesProcessMinBendRadius = true;
                    return astarPath;
                }
            }
        }

        // Coarse-grid retry (issue #1418): the fine A* can flood the huge free plane
        // between component rows and exhaust its budget before reaching an empty detour
        // lane. One retry per radius on a coarser grid (factor²-scaled budget) reaches
        // the lane; the result is only accepted after a collision check on the fine grid.
        // Skipped when the fine attempt's flood proved the goal region unreachable —
        // the coarser (strictly more blocked) grid cannot find a validatable route then.
        if (_lastFineAttemptGoalReachable)
        {
            var coarsePath = TryRouteCoarseAStar(effectiveRadius, startX, startY, startAngle,
                                                 endX, endY, endInputAngle, startPin, endPin,
                                                 cancellationToken);
            if (coarsePath != null)
            {
                return coarsePath;
            }
            if (floorRaisesRadius)
            {
                coarsePath = TryRouteCoarseAStar(connectionRadius, startX, startY, startAngle,
                                                 endX, endY, endInputAngle, startPin, endPin,
                                                 cancellationToken);
                if (coarsePath != null)
                {
                    coarsePath.ViolatesProcessMinBendRadius = true;
                    return coarsePath;
                }
            }
        }

        var fallbackPath = RouteManhattanFallback(startX, startY, startAngle, endX, endY, endInputAngle,
                                                  connectionRadius, effectiveRadius, floorRaisesRadius);
        if (fallbackPath.IsBlockedFallback)
        {
            fallbackPath.FailureReason = ClassifyBlockedFallback(startPin, endPin, effectiveRadius);
        }
        return fallbackPath;
    }

}
