using CAP.Avalonia.Services.GdsImport.FrozenRoutes;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.ImportedGeometry;
using CAP_DataAccess.Import.Gds;

namespace CAP.Avalonia.Services.GdsImport;

/// <summary>
/// The as-drawn half of <see cref="GdsPlacementExecutor"/>: hands every imported
/// top-cell polygon to the geometry it belongs to, so an untouched import renders
/// and exports exactly as drawn. Frozen route connections receive their core
/// polygons plus the matching cladding/trench layer pieces; leftover routing
/// polygons (junctions, unconnected stubs) and background polygons (logos, marks,
/// pads) become pin-less frozen paths — on the canvas for a flat import, inside the
/// group otherwise.
/// </summary>
public sealed partial class GdsPlacementExecutor
{
    /// <summary>Frozen centerline routes created by the current run, with their source polygons.</summary>
    private readonly List<(WaveguideConnection Connection, GdsCenterlineRoute Route, IReadOnlyList<GdsOutlinePolygon> Sources)> _centerlineRoutes = new();

    /// <summary>Frozen routes of the current run that kept the traced-outline fallback, with their source polygons.</summary>
    private readonly List<(WaveguideConnection Connection, IReadOnlyList<GdsOutlinePolygon> Sources)> _tracedRoutes = new();

    /// <summary>
    /// Assigns the drawn polygons: connection skins are attached in place; the returned
    /// leftover routing paths and background polygons (canvas coordinates) still need a
    /// home on the canvas or in a group.
    /// </summary>
    private (List<FrozenWaveguidePath> Leftovers, List<OutlinePolygon> Background) AssignAsDrawnGeometry(
        GdsPlacementPlan plan, GdsPlacementReport report, (double X, double Y) originOffset)
    {
        var assigner = new GdsAsDrawnGeometryAssigner();
        var connectionOwners = _centerlineRoutes
            .Select(r => (r.Connection, Owner: assigner.AddOwner(r.Route.Pieces, ToOutlines(r.Sources, originOffset))))
            .Concat(_tracedRoutes.Select(r => (r.Connection,
                Owner: assigner.AddOwner(Array.Empty<RibbonFit>(), ToOutlines(r.Sources, originOffset)))))
            .ToList();
        var leftovers = plan.TopCellWaveguidePolygons
            .Select(p => (Path: CreateLeftoverPath(p, originOffset, out var pieces), Pieces: pieces, Polygon: p))
            .Select(l => (l.Path, Owner: assigner.AddOwner(l.Pieces, new[] { GdsAsDrawnGeometryAssigner.ToOutline(l.Polygon, originOffset.X, originOffset.Y) })))
            .ToList();
        var background = assigner.Assign(plan.TopCellResidualPolygons, originOffset.X, originOffset.Y);

        foreach (var (connection, owner) in connectionOwners)
            connection.AttachAsDrawnGeometry(new AsDrawnGeometry(assigner.PolygonsOf(owner)));
        foreach (var (path, owner) in leftovers)
            path.AsDrawnGeometry = new AsDrawnGeometry(assigner.PolygonsOf(owner));

        report.FrozenRoutePathCount = leftovers.Count;
        report.BackgroundPolygonCount = background.Count;
        return (leftovers.Select(l => l.Path).ToList(), background);
    }

    /// <summary>
    /// Flat import: leftover routing paths and background polygons land directly on the
    /// canvas as pin-less frozen paths.
    /// </summary>
    private void AddCanvasFrozenPaths(List<FrozenWaveguidePath> leftovers, List<OutlinePolygon> background)
    {
        var paths = leftovers.Concat(background.Select(CreateBackgroundPath))
            .Select(p => new CanvasFrozenPathViewModel(p)).ToList();
        if (paths.Count > 0)
            Execute(new Commands.AddCanvasFrozenPathsCommand(_canvas, paths));
    }

    /// <summary>
    /// A leftover routing polygon as a pin-less frozen path: its fitted centerline when
    /// it is a clean ribbon (so its length and hit area are honest), the traced outline
    /// otherwise.
    /// </summary>
    private static FrozenWaveguidePath CreateLeftoverPath(
        GdsOutlinePolygon polygon, (double X, double Y) originOffset, out IReadOnlyList<RibbonFit> pieces)
    {
        var fit = RibbonCenterlineFitter.Fit(GdsCenterlineRouteBuilder.ToCanvas(polygon, originOffset.X, originOffset.Y));
        if (fit is null)
        {
            pieces = Array.Empty<RibbonFit>();
            return GdsFrozenRoutePathFactory.Create(polygon, originOffset.X, originOffset.Y);
        }

        pieces = new[] { fit };
        var path = new RoutedPath();
        path.Segments.AddRange(fit.Segments);
        return new FrozenWaveguidePath
        {
            Path = path,
            Layer = polygon.Layer,
            DataType = polygon.DataType,
            WidthMicrometers = fit.WidthMicrometers,
            IsRouteFrozen = true,
        };
    }

    /// <summary>A background polygon as a pin-less frozen path traced along its outline.</summary>
    private static FrozenWaveguidePath CreateBackgroundPath(OutlinePolygon polygon)
    {
        var path = GdsFrozenRoutePathFactory.Create(new GdsOutlinePolygon
        {
            Layer = polygon.Layer,
            DataType = polygon.DataType,
            Points = polygon.Points.Select(p => new GdsOutlinePoint(p.X, p.Y)).ToList(),
        });
        path.AsDrawnGeometry = new AsDrawnGeometry(new[] { polygon });
        return path;
    }

    private static IEnumerable<OutlinePolygon> ToOutlines(
        IEnumerable<GdsOutlinePolygon> polygons, (double X, double Y) originOffset) =>
        polygons.Select(p => GdsAsDrawnGeometryAssigner.ToOutline(p, originOffset.X, originOffset.Y));
}
