using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.MeanderGeneration;

/// <summary>
/// Applies a per-connection target length on a real design (issue #1008): derives a
/// <see cref="MeanderRequest"/> from a live <see cref="WaveguideConnection"/> — endpoint
/// poses from the pin positions and directions, the minimum bend radius from the
/// connection's process cross-section, the bounding rectangle from the free area around
/// the current route — runs the <see cref="MeanderPathGenerator"/> and, on success,
/// replaces the connection's route geometry. Typed failures pass through unchanged:
/// the route is never silently left as-is without a reason.
/// </summary>
public sealed class ConnectionLengthMatcher
{
    /// <summary>
    /// Clearance (µm) kept between the derived bounds and a component obstacle face,
    /// so the meandered geometry never grazes a neighboring component.
    /// </summary>
    private const double ObstacleClearanceMicrometers = 1.0;

    /// <summary>Floating-point slack (µm) when comparing a routed length against its target.</summary>
    private const double TargetComparisonSlackMicrometers = 1e-6;

    private readonly WaveguideRouter? _router;
    private readonly MeanderPathGenerator _generator = new();

    /// <summary>
    /// Creates a matcher. The optional router supplies the per-connection process
    /// bend-radius floor (<see cref="WaveguideRouter.ResolveProcessFloorFor"/>); without
    /// one, the connection's own <see cref="WaveguideConnection.BendRadiusMicrometers"/>
    /// alone governs the minimum radius.
    /// </summary>
    public ConnectionLengthMatcher(WaveguideRouter? router = null)
    {
        _router = router;
    }

    /// <summary>
    /// Stretches the connection's route to <paramref name="targetLengthMicrometers"/>
    /// ± <paramref name="toleranceMicrometers"/>. On success the route geometry is
    /// replaced and frozen (later recalculations keep it while the endpoints stay put)
    /// and the target/tolerance — plus the meander radius when one was given — are recorded
    /// on the connection. On failure the connection is left untouched and the returned
    /// <see cref="MeanderResult"/> carries the typed reason.
    /// </summary>
    /// <param name="connection">The connection whose route is stretched.</param>
    /// <param name="obstacleComponents">
    /// The design's components: they bound the free area around the current route and the
    /// meander may not enter any of them except the connection's own endpoint components.
    /// </param>
    /// <param name="targetLengthMicrometers">Target geometric length (µm).</param>
    /// <param name="toleranceMicrometers">Accepted deviation (µm).</param>
    /// <param name="siblings">
    /// The design's connections; the meander keeps the router's spacing to their routes and
    /// never crosses or touches one. Null ignores other routes.
    /// </param>
    /// <param name="meanderRadiusMicrometers">
    /// Bend radius of the meander loops; null uses the connection's own bend radius. Never
    /// below the process floor (<see cref="ResolveProcessFloor"/>).
    /// </param>
    public MeanderResult ApplyTargetLength(
        WaveguideConnection connection,
        IReadOnlyList<Component> obstacleComponents,
        double targetLengthMicrometers,
        double toleranceMicrometers,
        IEnumerable<WaveguideConnection>? siblings = null,
        double? meanderRadiusMicrometers = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(obstacleComponents);
        if (connection.StartPin == null || connection.EndPin == null)
        {
            return MeanderResult.Failure(
                MeanderFailureReason.EndpointsNotRoutableAtMinRadius,
                "The connection has no endpoint pins to derive poses from.");
        }

        var request = BuildRequest(connection, obstacleComponents, targetLengthMicrometers,
            toleranceMicrometers, siblings, meanderRadiusMicrometers);
        if (request.Bounds.MaxX <= request.Bounds.MinX || request.Bounds.MaxY <= request.Bounds.MinY)
        {
            return MeanderResult.Failure(
                MeanderFailureReason.BoundsTooSmallForMeander,
                "Component obstacles leave no free area around the current route.");
        }

        var result = _generator.Generate(request);
        if (!result.IsSuccess)
            return result;

        connection.ReplaceRoutedPath(result.Path!);
        connection.IsRouteFrozen = true;
        connection.TargetLengthMicrometers = targetLengthMicrometers;
        connection.LengthToleranceMicrometers = toleranceMicrometers;
        if (meanderRadiusMicrometers is { } radius)
            connection.BendRadiusMicrometers = radius;
        return result;
    }

    /// <summary>
    /// Re-establishes every recorded length target the current geometry no longer meets — e.g.
    /// after an endpoint move unfroze the meander and the router replaced it — keeping clear of
    /// all other routes. Connections whose target cannot be met keep their route; the design
    /// checks report them.
    /// </summary>
    /// <param name="connections">Every connection of the design (they are also the obstacles).</param>
    /// <param name="obstacleComponents">The design's components.</param>
    /// <param name="isCandidate">
    /// Limits which connections are stretched again, e.g. to the routes a pass just replaced; a
    /// target that failed before at unchanged geometry fails again. Null tries every unmet one.
    /// </param>
    /// <returns>The connections whose route geometry was replaced.</returns>
    public IReadOnlyList<WaveguideConnection> ReapplyUnmetTargets(
        IReadOnlyList<WaveguideConnection> connections, IReadOnlyList<Component> obstacleComponents,
        Func<WaveguideConnection, bool>? isCandidate = null)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(obstacleComponents);
        var changed = new List<WaveguideConnection>();
        foreach (var connection in connections)
        {
            if (connection.TargetLengthMicrometers is not { } target || MeetsTarget(connection))
                continue;
            if (isCandidate != null && !isCandidate(connection))
                continue;
            double tolerance = connection.LengthToleranceMicrometers ?? 0.0;
            if (ApplyTargetLength(connection, obstacleComponents, target, tolerance, connections).IsSuccess)
                changed.Add(connection);
        }
        return changed;
    }

    /// <summary>
    /// True when the connection has no length target, or its routed length is within the
    /// recorded tolerance of it.
    /// </summary>
    public static bool MeetsTarget(WaveguideConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.TargetLengthMicrometers is not { } target)
            return true;
        if (connection.RoutedPath is not { Segments.Count: > 0 })
            return false;
        double tolerance = Math.Max(connection.LengthToleranceMicrometers ?? 0.0, TargetComparisonSlackMicrometers);
        return Math.Abs(connection.PathLengthMicrometers - target) <= tolerance;
    }

    /// <summary>
    /// The process minimum bend radius (µm) for the connection's pin pair; 0 when no router
    /// supplies one.
    /// </summary>
    public double ResolveProcessFloor(WaveguideConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (_router == null || connection.StartPin == null || connection.EndPin == null)
            return 0.0;
        return _router.ResolveProcessFloorFor(connection.StartPin, connection.EndPin);
    }

    /// <summary>
    /// Derives the meander request for a connection: poses from the pin positions and
    /// directions (the end direction is the direction of travel at arrival — the end
    /// pin's outward normal reversed), the bend radius from the request or the connection
    /// (never below the process floor), the bounding rectangle from the current route's
    /// bounding box inflated toward the nearest component obstacle on each side, and the
    /// obstacles the meander must keep clear of.
    /// </summary>
    public MeanderRequest BuildRequest(
        WaveguideConnection connection,
        IReadOnlyList<Component> obstacleComponents,
        double targetLengthMicrometers,
        double toleranceMicrometers,
        IEnumerable<WaveguideConnection>? siblings = null,
        double? meanderRadiusMicrometers = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(obstacleComponents);
        if (connection.StartPin == null || connection.EndPin == null)
            throw new ArgumentException("The connection must have both endpoint pins.", nameof(connection));

        var (startX, startY) = connection.StartPin.GetAbsolutePosition();
        var (endX, endY) = connection.EndPin.GetAbsolutePosition();
        double startDirection = connection.StartPin.GetAbsoluteAngle();
        double endDirection = (connection.EndPin.GetAbsoluteAngle() + 180.0) % 360.0;

        double floor = ResolveProcessFloor(connection);
        double loopRadius = Math.Max(meanderRadiusMicrometers ?? connection.BendRadiusMicrometers, floor);
        double minRadius = floor > 0 ? floor : loopRadius;

        // With siblings the obstacles carry every component footprint, so the bounds only cap the
        // reach; clamping them to the nearest component as well would rule out room that exists.
        var baseBounds = CurrentRouteBounds(connection, startX, startY, endX, endY);
        double margin = InflationMargin(targetLengthMicrometers, loopRadius);
        var bounds = siblings == null
            ? InflateTowardObstacles(baseBounds, margin, obstacleComponents)
            : new MeanderBounds(baseBounds.MinX - margin, baseBounds.MinY - margin,
                baseBounds.MaxX + margin, baseBounds.MaxY + margin);
        var obstacles = siblings == null
            ? null
            : MeanderObstacleCollector.Collect(connection, siblings, obstacleComponents,
                _router?.MinWaveguideSpacingMicrometers ?? PhotonicConstants.DefaultMinWaveguideSpacingMicrometers);

        return new MeanderRequest(
            startX, startY, startDirection,
            endX, endY, endDirection,
            targetLengthMicrometers, toleranceMicrometers,
            minRadius, bounds, obstacles, connection.RoutedPath, loopRadius);
    }

    /// <summary>
    /// Per-side inflation of the route's bounding box. A meander's perpendicular reach is
    /// largest when the whole extra length goes into a single hairpin loop
    /// (extra/2 + 2r, and extra never exceeds the target), so this margin always leaves
    /// room for the tightest fold; obstacle clamping keeps it inside the free area.
    /// Keying the margin off the target (not the current route length) keeps the derived
    /// bounds free of the route's present shape, so a saved-and-reloaded connection
    /// re-derives the identical meander geometry.
    /// </summary>
    private static double InflationMargin(double targetLengthMicrometers, double minRadiusMicrometers)
        => Math.Max(0.0, targetLengthMicrometers) / 2.0
        + 2.0 * minRadiusMicrometers
        + ObstacleClearanceMicrometers;

    /// <summary>
    /// Tight bounding box of the connection's current route geometry, always containing
    /// both pin positions (a routeless connection degrades to the pin-to-pin box).
    /// </summary>
    private static (double MinX, double MinY, double MaxX, double MaxY) CurrentRouteBounds(
        WaveguideConnection connection, double startX, double startY, double endX, double endY)
    {
        double minX = Math.Min(startX, endX);
        double minY = Math.Min(startY, endY);
        double maxX = Math.Max(startX, endX);
        double maxY = Math.Max(startY, endY);

        if (connection.RoutedPath != null)
        {
            foreach (var segment in connection.RoutedPath.Segments)
            {
                var b = PathSegmentBounds.Of(segment);
                minX = Math.Min(minX, b.MinX);
                minY = Math.Min(minY, b.MinY);
                maxX = Math.Max(maxX, b.MaxX);
                maxY = Math.Max(maxY, b.MaxY);
            }
        }

        return (minX, minY, maxX, maxY);
    }

    /// <summary>
    /// Inflates the base box by up to <paramref name="margin"/> per side, clamped so the
    /// result never reaches closer than <see cref="ObstacleClearanceMicrometers"/> to a
    /// component. Components the route already touches (its endpoint components) overlap
    /// the base box and are anchors of the route, not obstacles to it. The perpendicular
    /// overlap test uses the fully inflated range — conservative: a side may be clamped
    /// by an obstacle that only the inflation on the OTHER axis would have reached.
    /// </summary>
    private static MeanderBounds InflateTowardObstacles(
        (double MinX, double MinY, double MaxX, double MaxY) baseBounds,
        double margin,
        IReadOnlyList<Component> components)
    {
        double xMinus = margin;
        double xPlus = margin;
        double yMinus = margin;
        double yPlus = margin;

        foreach (var component in components)
        {
            if (!component.IsRoutingObstacle)
                continue;

            double rectMinX = component.PhysicalX;
            double rectMinY = component.PhysicalY;
            double rectMaxX = component.PhysicalX + component.WidthMicrometers;
            double rectMaxY = component.PhysicalY + component.HeightMicrometers;

            bool touchesRoute = rectMinX <= baseBounds.MaxX && rectMaxX >= baseBounds.MinX
                && rectMinY <= baseBounds.MaxY && rectMaxY >= baseBounds.MinY;
            if (touchesRoute)
                continue;

            bool overlapsY = rectMinY < baseBounds.MaxY + margin && rectMaxY > baseBounds.MinY - margin;
            if (overlapsY)
            {
                if (rectMinX >= baseBounds.MaxX)
                    xPlus = Math.Min(xPlus, Math.Max(0.0, rectMinX - baseBounds.MaxX - ObstacleClearanceMicrometers));
                if (rectMaxX <= baseBounds.MinX)
                    xMinus = Math.Min(xMinus, Math.Max(0.0, baseBounds.MinX - rectMaxX - ObstacleClearanceMicrometers));
            }

            bool overlapsX = rectMinX < baseBounds.MaxX + margin && rectMaxX > baseBounds.MinX - margin;
            if (overlapsX)
            {
                if (rectMinY >= baseBounds.MaxY)
                    yPlus = Math.Min(yPlus, Math.Max(0.0, rectMinY - baseBounds.MaxY - ObstacleClearanceMicrometers));
                if (rectMaxY <= baseBounds.MinY)
                    yMinus = Math.Min(yMinus, Math.Max(0.0, baseBounds.MinY - rectMaxY - ObstacleClearanceMicrometers));
            }
        }

        return new MeanderBounds(
            baseBounds.MinX - xMinus, baseBounds.MinY - yMinus,
            baseBounds.MaxX + xPlus, baseBounds.MaxY + yPlus);
    }
}
