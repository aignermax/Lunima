using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP.Avalonia.Services.GdsImport.LayerVisibility;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Draws imported component outline polygons (e.g. from a GDS-imported PDK
/// component) in place of the plain rectangle body. Outline points are stored
/// in the component's local frame (µm, Y-down, relative to the unrotated bbox
/// top-left), so they map 1:1 onto the world-space footprint the rest of the
/// canvas renders in. Rotation reuses the exact mechanism of
/// <see cref="GdsPolygonRenderer"/>: geometry stays unrotated, the destination
/// rect is un-swapped via <see cref="GdsPolygonRenderer.GetUnrotatedDestRect"/>,
/// and <see cref="GdsPolygonRenderer.BuildRotationMatrix"/> rotates around the
/// footprint centre — the same centre and direction the rotate command uses
/// for pins.
/// </summary>
internal sealed class ComponentOutlineRenderer
{
    // Geometry is cached per outline list (below); the per-layer brushes/pens come
    // from OutlineLayerPalette's static cache — never allocated per frame.
    // v2 styles per (layer, datatype); the v1 single blue survives as the palette's
    // waveguide-core entry (1, 0).

    /// <summary>
    /// Geometry cache keyed by the outline list instance. All placed instances of
    /// one template share that list (see <c>ComponentTemplates.CreateFromTemplate</c>),
    /// so geometry is built once per component type, never per frame; the weak
    /// table drops the entry when the template is unloaded.
    /// </summary>
    private readonly ConditionalWeakTable<IReadOnlyList<OutlinePolygon>, OutlineBatch[]> _geometryCache = new();

    private readonly OutlineRasterCache? _rasterCache;

    /// <summary>Creates the renderer.</summary>
    /// <param name="useRasterCache">
    /// True (default) draws zoomed-out outlines from cached bitmaps (<see cref="OutlineRasterCache"/>);
    /// false always draws vectors (tests that inspect the per-polygon level of detail).
    /// </param>
    public ComponentOutlineRenderer(bool useRasterCache = true)
    {
        _rasterCache = useRasterCache ? new OutlineRasterCache() : null;
    }

    /// <summary>Test seam (InternalsVisibleTo UnitTests): batched geometries actually issued
    /// to <see cref="DrawingContext"/> since the last <see cref="ResetDrawCounters"/> (one per
    /// layer and size class, see <see cref="OutlineGeometryBatcher"/>).</summary>
    internal long IssuedGeometryCount { get; private set; }

    /// <summary>Test seam (InternalsVisibleTo UnitTests): polygons skipped by the LOD cull
    /// since the last <see cref="ResetDrawCounters"/>.</summary>
    internal long CulledGeometryCount { get; private set; }

    /// <summary>Test seam (InternalsVisibleTo UnitTests): zeroes both draw counters.</summary>
    internal void ResetDrawCounters() => (IssuedGeometryCount, CulledGeometryCount) = (0, 0);

    /// <summary>
    /// Draws <paramref name="outlines"/> for <paramref name="comp"/>. Caller must
    /// guarantee a non-empty list — the fallback rectangle path lives in
    /// <see cref="ComponentRenderer"/>.
    /// </summary>
    /// <param name="zoom">Current canvas zoom, used only for the per-polygon LOD cull
    /// (see <see cref="RenderCulling.IsBelowOutlineLodThreshold"/>).</param>
    /// <param name="layerVisibility">Per-design layer view filter (issue #858);
    /// null renders every layer fully visible.</param>
    public void Draw(DrawingContext context, ComponentViewModel comp, IReadOnlyList<OutlinePolygon> outlines, bool isDimmed, double zoom,
        GdsLayerVisibilityState? layerVisibility = null, Rect? visibleWorld = null) =>
        Draw(context, comp.X, comp.Y, comp.Width, comp.Height,
            comp.Component.RotationDegrees, outlines, isDimmed, zoom,
            comp.Component.UnrotatedWidthMicrometers, comp.Component.UnrotatedHeightMicrometers,
            layerVisibility, comp.Component.IsMirroredHorizontally, visibleWorld);

    /// <summary>
    /// Pose-based overload for callers that have no <see cref="ComponentViewModel"/>:
    /// group children are rendered straight from their core component pose
    /// (<see cref="ComponentRenderer"/> flattens groups itself). Caller must guarantee
    /// a non-empty list — the fallback rectangle path lives in
    /// <see cref="ComponentRenderer"/>.
    /// </summary>
    /// <param name="zoom">Current canvas zoom, used only for the per-polygon LOD cull
    /// (see <see cref="RenderCulling.IsBelowOutlineLodThreshold"/>).</param>
    /// <param name="recordedUnrotatedWidth">Recorded pre-rotation footprint width (0 when
    /// never rotated or legacy); see <c>Component.UnrotatedWidthMicrometers</c>.</param>
    /// <param name="recordedUnrotatedHeight">Recorded pre-rotation footprint height.</param>
    /// <param name="layerVisibility">Per-design layer view filter (issue #858):
    /// polygons on hidden layers are skipped, faded layers draw with reduced
    /// opacity. Null renders every layer fully visible.</param>
    /// <param name="mirrored">True for a mirrored component (GDS STRANS reflection): the
    /// outline is reflected across the horizontal centreline of its unrotated frame before
    /// it is rotated — the same mirror the component's pins carry.</param>
    /// <param name="visibleWorld">The visible world rectangle, or null to draw everything:
    /// batches (tiled, see <see cref="OutlineGeometryBatcher"/>) entirely outside it are
    /// skipped, so zooming into a corner of a huge cell no longer processes all of it.</param>
    public void Draw(DrawingContext context, double x, double y, double width, double height,
        double rotationDegrees, IReadOnlyList<OutlinePolygon> outlines, bool isDimmed, double zoom,
        double recordedUnrotatedWidth = 0, double recordedUnrotatedHeight = 0,
        GdsLayerVisibilityState? layerVisibility = null, bool mirrored = false, Rect? visibleWorld = null)
    {
        var geometries = _geometryCache.GetValue(outlines, OutlineGeometryBatcher.Build);

        double centerX = x + width / 2.0;
        double centerY = y + height / 2.0;
        var destRect = GdsPolygonRenderer.GetUnrotatedDestRect(
            x, y, width, height, rotationDegrees, recordedUnrotatedWidth, recordedUnrotatedHeight);
        var transform = LocalMirror(mirrored, destRect.Height)
                      * Matrix.CreateTranslation(destRect.X, destRect.Y)
                      * GdsPolygonRenderer.BuildRotationMatrix(rotationDegrees, centerX, centerY);

        var localVisible = visibleWorld is { } world ? ToLocal(world, transform) : null;
        using (context.PushTransform(transform))
        // An opacity push renders into an offscreen layer: only pay for it when dimmed.
        using (isDimmed ? context.PushOpacity(128.0 / 255.0) : (IDisposable?)null)
        {
            if (_rasterCache?.TryDraw(context, outlines, geometries, zoom, layerVisibility) == true)
            {
                IssuedGeometryCount++;
                return;
            }
            foreach (var cached in geometries)
            {
                if (localVisible is { } visible && !visible.Intersects(cached.Bounds))
                    continue;

                // Pure view filter (#858): a hidden layer draws nothing, a faded
                // layer draws through an extra opacity push. Deliberately outside
                // the LOD counters — hiding is a user choice, not a perf cull.
                double layerOpacity = layerVisibility?.EffectiveOpacity(cached.Layer, cached.DataType) ?? 1.0;
                if (layerOpacity <= 0)
                    continue;

                // The pushed transform is rigid, so a polygon's local extent × zoom is
                // its on-screen size: at full zoom-out most polygons of a huge import
                // are sub-pixel specks. A batch is skipped when even its largest
                // member is below the threshold.
                if (RenderCulling.IsBelowOutlineLodThreshold(cached.MaxPolygonExtent, cached.MaxPolygonExtent, zoom))
                {
                    CulledGeometryCount += cached.PolygonCount;
                    continue;
                }
                IssuedGeometryCount++;
                if (layerOpacity < 1.0)
                {
                    using (context.PushOpacity(layerOpacity))
                        context.DrawGeometry(cached.Fill, cached.Outline, cached.Geometry);
                }
                else
                {
                    context.DrawGeometry(cached.Fill, cached.Outline, cached.Geometry);
                }
            }
        }
    }

    /// <summary>
    /// Transforms one outline point from the component's local frame to world
    /// coordinates for the given component pose — the same mapping
    /// <see cref="Draw"/> applies through the pushed transform. Exposed as
    /// <c>internal</c> to allow transform-math unit tests.
    /// </summary>
    internal static Point TransformOutlinePoint(
        OutlinePoint point,
        double compX, double compY,
        double compWidth, double compHeight,
        double rotationDegrees,
        double recordedUnrotatedWidth = 0, double recordedUnrotatedHeight = 0,
        bool mirrored = false)
    {
        var destRect = GdsPolygonRenderer.GetUnrotatedDestRect(
            compX, compY, compWidth, compHeight, rotationDegrees,
            recordedUnrotatedWidth, recordedUnrotatedHeight);
        var rotation = GdsPolygonRenderer.BuildRotationMatrix(
            rotationDegrees, compX + compWidth / 2.0, compY + compHeight / 2.0);
        double localY = mirrored ? destRect.Height - point.Y : point.Y;
        return new Point(destRect.X + point.X, destRect.Y + localY).Transform(rotation);
    }

    /// <summary>The axis-aligned box of <paramref name="world"/> in the component's local frame.</summary>
    private static Rect? ToLocal(Rect world, Matrix localToWorld)
    {
        if (!localToWorld.TryInvert(out var worldToLocal)) return null;
        var a = world.TopLeft.Transform(worldToLocal);
        var b = world.TopRight.Transform(worldToLocal);
        var c = world.BottomLeft.Transform(worldToLocal);
        var d = world.BottomRight.Transform(worldToLocal);
        double minX = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)), maxX = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        double minY = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)), maxY = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Reflection across the horizontal centreline of an unrotated frame of the given
    /// height (y → height − y), or identity when not mirrored.
    /// </summary>
    private static Matrix LocalMirror(bool mirrored, double height) =>
        mirrored ? new Matrix(1, 0, 0, -1, 0, height) : Matrix.Identity;

    /// <summary>
    /// World-space points of one outline polygon for the given component pose.
    /// The ring stays closed: with the GDS convention (first point repeated at
    /// the end) the first and last world points coincide.
    /// </summary>
    internal static Point[] ComputeWorldPoints(
        OutlinePolygon polygon,
        double compX, double compY,
        double compWidth, double compHeight,
        double rotationDegrees,
        double recordedUnrotatedWidth = 0, double recordedUnrotatedHeight = 0,
        bool mirrored = false)
    {
        var points = new Point[polygon.Points.Count];
        for (int i = 0; i < polygon.Points.Count; i++)
            points[i] = TransformOutlinePoint(
                polygon.Points[i], compX, compY, compWidth, compHeight, rotationDegrees,
                recordedUnrotatedWidth, recordedUnrotatedHeight, mirrored);
        return points;
    }
}
