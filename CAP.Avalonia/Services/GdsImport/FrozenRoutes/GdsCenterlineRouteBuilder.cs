using CAP_Core.Routing;
using CAP_Core.Routing.ImportedGeometry;
using CAP_DataAccess.Import.Gds;

namespace CAP.Avalonia.Services.GdsImport.FrozenRoutes;

/// <summary>
/// The centerline route recovered from an imported connection's drawn polygons.
/// </summary>
/// <param name="Path">Continuous path from the start pin to the end pin (canvas µm).</param>
/// <param name="WidthMicrometers">Waveguide width of the chosen (core) layer.</param>
/// <param name="Layer">GDS layer of the polygons the centerline was fitted from.</param>
/// <param name="DataType">GDS datatype of those polygons.</param>
/// <param name="Pieces">The fitted pieces in canvas space, one per source polygon of the chosen layer.</param>
public sealed record GdsCenterlineRoute(
    RoutedPath Path,
    double WidthMicrometers,
    int Layer,
    int DataType,
    IReadOnlyList<RibbonFit> Pieces);

/// <summary>
/// Turns the polygons a GDS route network was drawn with into a real centerline
/// route: every polygon is fitted as a straight, arc or polyline ribbon
/// (<see cref="RibbonCenterlineFitter"/>) and the pieces are chained from pin to
/// pin (<see cref="FrozenRouteChainer"/>). Routes are often drawn on several
/// layers at once (core plus cladding/trench layers with the same centerline);
/// each layer is tried on its own and the narrowest one that chains cleanly wins,
/// because the core sets the optical width.
/// </summary>
public static class GdsCenterlineRouteBuilder
{
    /// <summary>
    /// Builds the centerline route, or returns null when no layer's polygons are
    /// all ribbons that chain from <paramref name="startUm"/> to <paramref name="endUm"/>.
    /// </summary>
    /// <param name="polygons">The network's route polygons, in plan space.</param>
    /// <param name="startUm">Start pin's absolute canvas position (µm).</param>
    /// <param name="endUm">End pin's absolute canvas position (µm).</param>
    /// <param name="offsetXUm">Plan-to-canvas X translation (µm).</param>
    /// <param name="offsetYUm">Plan-to-canvas Y translation (µm).</param>
    public static GdsCenterlineRoute? TryBuild(
        IReadOnlyList<GdsOutlinePolygon> polygons,
        (double X, double Y) startUm,
        (double X, double Y) endUm,
        double offsetXUm = 0.0,
        double offsetYUm = 0.0)
    {
        ArgumentNullException.ThrowIfNull(polygons);
        GdsCenterlineRoute? best = null;
        foreach (var layer in polygons.GroupBy(p => (p.Layer, p.DataType)))
        {
            var candidate = TryBuildLayer(layer.ToList(), layer.Key, startUm, endUm, offsetXUm, offsetYUm);
            if (candidate is not null && (best is null || candidate.WidthMicrometers < best.WidthMicrometers))
                best = candidate;
        }
        return best;
    }

    private static GdsCenterlineRoute? TryBuildLayer(
        List<GdsOutlinePolygon> polygons,
        (int Layer, int DataType) key,
        (double X, double Y) startUm,
        (double X, double Y) endUm,
        double offsetXUm,
        double offsetYUm)
    {
        var pieces = new List<RibbonFit>(polygons.Count);
        foreach (var polygon in polygons)
        {
            var fit = RibbonCenterlineFitter.Fit(ToCanvas(polygon, offsetXUm, offsetYUm));
            if (fit is null) return null;
            pieces.Add(fit);
        }

        var path = FrozenRouteChainer.Chain(pieces, startUm, endUm);
        if (path is null) return null;
        double width = pieces.Average(p => p.WidthMicrometers);
        return new GdsCenterlineRoute(path, width, key.Layer, key.DataType, pieces);
    }

    /// <summary>The polygon's outline translated into canvas space.</summary>
    public static List<(double X, double Y)> ToCanvas(GdsOutlinePolygon polygon, double offsetXUm, double offsetYUm) =>
        polygon.Points.Select(p => (p.X + offsetXUm, p.Y + offsetYUm)).ToList();
}
