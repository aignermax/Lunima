namespace CAP_Core.Routing.MeanderGeneration;

/// <summary>
/// Exact geometric verdict for a meander candidate against <see cref="MeanderObstacles"/>,
/// using the same three rules the direct-route policy applies to sibling routes: no crossing,
/// no contact anywhere, and the minimum spacing outside the pin fan-out zones.
/// Only the geometry the meander actually changed is judged, so a conflict the existing route
/// already had is not blamed on the meander.
/// </summary>
internal static class MeanderCollisionChecker
{
    /// <summary>Footprint shrink (µm) so a route ending on a component edge never counts as entering it.</summary>
    private const double FootprintEdgeToleranceMicrometers = 1e-3;

    /// <summary>
    /// True when <paramref name="changed"/> conflicts with any obstacle.
    /// </summary>
    /// <param name="changed">The new geometry of the candidate (the whole path when it was rebuilt pin to pin).</param>
    /// <param name="obstacles">Routes and footprints to keep clear of.</param>
    /// <param name="pinExemptionRadiusMicrometers">
    /// Radius around the probe's own endpoints where sub-spacing proximity is tolerated (the pins'
    /// fixed pitch); 0 when the probe starts and ends mid-route.
    /// </param>
    public static bool Collides(RoutedPath changed, MeanderObstacles obstacles, double pinExemptionRadiusMicrometers)
    {
        if (changed.Segments.Count == 0 || obstacles.IsEmpty)
            return false;

        foreach (var footprint in obstacles.Footprints)
        {
            if (PathIntersectionDetector.IntersectsRectangle(changed,
                    footprint.MinX + FootprintEdgeToleranceMicrometers, footprint.MinY + FootprintEdgeToleranceMicrometers,
                    footprint.MaxX - FootprintEdgeToleranceMicrometers, footprint.MaxY - FootprintEdgeToleranceMicrometers))
                return true;
        }

        foreach (var route in obstacles.Routes)
        {
            if (route.Segments.Count == 0)
                continue;
            if (PathIntersectionDetector.Crosses(changed, route))
                return true;
            if (PathIntersectionDetector.ComesCloserThan(changed, route,
                    obstacles.MinSpacingMicrometers, pinExemptionRadiusMicrometers))
                return true;
            if (PathIntersectionDetector.ComesCloserThan(changed, route,
                    obstacles.ContactClearanceMicrometers, endpointExemptionRadiusMicrometers: 0))
                return true;
        }
        return false;
    }
}
