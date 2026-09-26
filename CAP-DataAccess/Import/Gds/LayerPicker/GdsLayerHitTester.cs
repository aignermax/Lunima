namespace CAP_DataAccess.Import.Gds.LayerPicker;

/// <summary>
/// Resolves a click position (micrometers, Y-up) on a
/// <see cref="GdsLayerGeometryScene"/> to the (layer, datatype) pair of the
/// geometry under the cursor. Text anchors win within their tolerance radius
/// (labels are tiny targets sitting ON other layers' polygons); among
/// containing polygons the smallest area wins, so a pad on top of a big slab
/// picks the pad, not the slab.
/// </summary>
public static class GdsLayerHitTester
{
    /// <summary>
    /// Attempts to resolve the layer pair at (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    /// <param name="scene">The scene to hit-test.</param>
    /// <param name="x">Click X in micrometers.</param>
    /// <param name="y">Click Y in micrometers, GDS Y-up.</param>
    /// <param name="textToleranceUm">Pick radius around text anchors in micrometers.</param>
    /// <param name="hit">The resolved pair on success.</param>
    /// <returns>True when a text anchor or polygon was hit.</returns>
    public static bool TryPick(
        GdsLayerGeometryScene scene, double x, double y, double textToleranceUm, out GdsLayerPair hit)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (TryPickText(scene, x, y, textToleranceUm, out hit))
            return true;
        return TryPickPolygon(scene, x, y, out hit);
    }

    private static bool TryPickText(
        GdsLayerGeometryScene scene, double x, double y, double toleranceUm, out GdsLayerPair hit)
    {
        hit = default;
        var bestDistanceSquared = toleranceUm * toleranceUm;
        var found = false;
        foreach (var layer in scene.Layers)
        {
            foreach (var text in layer.Texts)
            {
                var dx = text.Position.X - x;
                var dy = text.Position.Y - y;
                var distanceSquared = dx * dx + dy * dy;
                if (distanceSquared <= bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    hit = layer.Pair;
                    found = true;
                }
            }
        }
        return found;
    }

    private static bool TryPickPolygon(
        GdsLayerGeometryScene scene, double x, double y, out GdsLayerPair hit)
    {
        hit = default;
        var bestArea = double.MaxValue;
        var found = false;
        foreach (var layer in scene.Layers)
        {
            foreach (var polygon in layer.Polygons)
            {
                if (!ContainsPoint(polygon.Points, x, y))
                    continue;
                var area = Math.Abs(SignedArea(polygon.Points));
                if (area < bestArea)
                {
                    bestArea = area;
                    hit = layer.Pair;
                    found = true;
                }
            }
        }
        return found;
    }

    /// <summary>Even-odd ray-casting point-in-polygon test (closed or open point list).</summary>
    private static bool ContainsPoint(IReadOnlyList<GdsPoint> points, double x, double y)
    {
        var inside = false;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            var pi = points[i];
            var pj = points[j];
            var crossesRay = (pi.Y > y) != (pj.Y > y)
                && x < (pj.X - pi.X) * (y - pi.Y) / (pj.Y - pi.Y) + pi.X;
            if (crossesRay)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Shoelace signed area of the polygon.</summary>
    private static double SignedArea(IReadOnlyList<GdsPoint> points)
    {
        double sum = 0;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            sum += (points[j].X - points[i].X) * (points[j].Y + points[i].Y);
        return sum / 2.0;
    }
}
