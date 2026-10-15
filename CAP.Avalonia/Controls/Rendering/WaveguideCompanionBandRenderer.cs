using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.Services.WaveguideCompanions;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Routing;
using PathSegment = CAP_Core.Routing.PathSegment;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Draws the process companion layers (cladding / trench / keep-out bands) of routed
/// waveguides as translucent envelopes under the cores, in the same per-layer color and
/// fill alpha imported GDS polygons on that layer get (<see cref="OutlineLayerPalette"/>),
/// so Lunima's own waveguides and imported ones look alike. All paths sharing a
/// (layer, datatype, band width) are stroked as ONE geometry, so overlapping bands of
/// neighbouring wires merge like the mask union instead of stacking their alpha. Bands
/// honour the per-layer visibility filter of the GDS layer panel.
/// </summary>
public sealed class WaveguideCompanionBandRenderer : ICanvasRenderer
{
    /// <summary>Fill alpha of a band — the imported-outline fill alpha, so both read alike.</summary>
    private const byte BandAlpha = 46;

    /// <summary>Arc sampling step in degrees (matches the connection renderer's arcs).</summary>
    private const double ArcStepDegrees = 5.0;

    /// <summary>Two segment ends closer than this (µm) continue one figure.</summary>
    private const double JoinToleranceMicrometers = 1e-3;

    private readonly record struct BandKey(int Layer, int DataType, double Width);

    private readonly Dictionary<BandKey, Pen> _pens = new();

    /// <summary>Test seam (InternalsVisibleTo UnitTests): band geometries stroked by the last render.</summary>
    internal int LastBandBatchCount { get; private set; }

    /// <inheritdoc/>
    public void Render(DrawingContext context, CanvasRenderContext rc)
    {
        var vm = rc.ViewModel;
        LastBandBatchCount = 0;
        if (vm.WaveguideCompanionSpecProvider == null)
            return;

        var cull = RenderCulling.InflateForCulling(
            RenderCulling.ComputeViewportWorld(vm.PanX, vm.PanY, rc.Bounds, rc.Zoom), rc.Zoom);
        var batches = new Dictionary<BandKey, List<IReadOnlyList<PathSegment>>>();
        foreach (var path in WaveguideCompanionPathCollector.Collect(vm))
        {
            if (path.Segments.Count == 0 || !cull.Intersects(RenderCulling.ComputeSegmentBounds(path.Segments)))
                continue;
            AddToBatches(vm, path, batches);
        }

        foreach (var (key, paths) in batches)
        {
            double opacity = rc.LayerVisibility?.EffectiveOpacity(key.Layer, key.DataType) ?? 1.0;
            if (opacity <= 0)
                continue;
            DrawBatch(context, PenFor(key), paths, opacity);
            LastBandBatchCount++;
        }
    }

    private static void AddToBatches(
        DesignCanvasViewModel vm, WaveguideCompanionPath path,
        Dictionary<BandKey, List<IReadOnlyList<PathSegment>>> batches)
    {
        foreach (var layer in vm.GetWaveguideCompanions(path.StartPin, path.EndPin).Layers)
        {
            var key = new BandKey(layer.GdsLayer, layer.GdsDataType,
                Math.Round(layer.BandWidthFor(path.CoreWidthMicrometers), 3));
            if (!batches.TryGetValue(key, out var list))
            {
                list = new List<IReadOnlyList<PathSegment>>();
                batches[key] = list;
            }
            list.Add(path.Segments);
        }
    }

    private Pen PenFor(BandKey key)
    {
        if (_pens.TryGetValue(key, out var pen))
            return pen;
        var color = OutlineLayerPalette.ColorFor(key.Layer, key.DataType);
        pen = new Pen(CanvasPalette.Brush(CanvasPalette.WithAlpha(color, BandAlpha)), key.Width)
        {
            LineCap = PenLineCap.Flat,
            LineJoin = PenLineJoin.Round,
        };
        _pens[key] = pen;
        return pen;
    }

    private static void DrawBatch(
        DrawingContext context, Pen pen, List<IReadOnlyList<PathSegment>> paths, double opacity)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            foreach (var segments in paths)
                AppendFigures(g, segments);
        }

        if (opacity < 1.0)
        {
            using (context.PushOpacity(opacity))
                context.DrawGeometry(null, pen, geometry);
        }
        else
        {
            context.DrawGeometry(null, pen, geometry);
        }
    }

    private static void AppendFigures(StreamGeometryContext g, IReadOnlyList<PathSegment> segments)
    {
        Point? current = null;
        foreach (var segment in segments)
        {
            var start = new Point(segment.StartPoint.X, segment.StartPoint.Y);
            if (current is not { } at || Distance(at, start) > JoinToleranceMicrometers)
            {
                if (current != null)
                    g.EndFigure(false);
                g.BeginFigure(start, false);
            }

            if (segment is BendSegment bend)
                AppendArc(g, bend);
            else
                g.LineTo(new Point(segment.EndPoint.X, segment.EndPoint.Y));
            current = new Point(segment.EndPoint.X, segment.EndPoint.Y);
        }
        if (current != null)
            g.EndFigure(false);
    }

    private static void AppendArc(StreamGeometryContext g, BendSegment bend)
    {
        int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(bend.SweepAngleDegrees) / ArcStepDegrees));
        double sign = Math.Sign(bend.SweepAngleDegrees) == 0 ? 1 : Math.Sign(bend.SweepAngleDegrees);
        double startRad = bend.StartAngleDegrees * Math.PI / 180;
        double sweepRad = bend.SweepAngleDegrees * Math.PI / 180;
        for (int i = 1; i <= steps; i++)
        {
            double angle = startRad + sweepRad * i / steps - Math.PI / 2 * sign;
            g.LineTo(new Point(
                bend.Center.X + bend.RadiusMicrometers * Math.Cos(angle),
                bend.Center.Y + bend.RadiusMicrometers * Math.Sin(angle)));
        }
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
