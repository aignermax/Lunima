using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Logic.GateView;
using CAP.Avalonia.ViewModels.Logic.GateView.Layout;
using CAP_Core.Analysis.LogicAnalysis.GateView;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls.Canvas.GateView;

/// <summary>
/// Paints the Logic workspace's gate view in place of the physical design: nets first,
/// then gate symbols on top (a wire passing a gate disappears under it, as on paper),
/// then labels on the canvas's deferred label layer. The scene is laid out from the live
/// group positions and cached until a gate moves or the graph changes, so idle frames and
/// pans cost only the drawing.
/// </summary>
internal sealed class GateViewRenderer
{
    private const double JunctionRadiusPx = 3.0;
    private const double FlagLengthPx = 14.0;

    private GateViewGraph? _sceneGraph;
    private int _sceneFingerprint;
    private GateViewScene _scene = GateViewScene.Empty;

    /// <summary>The scene of the last frame (test seam).</summary>
    internal GateViewScene LastScene => _scene;

    /// <summary>Draws the gate view in world coordinates (inside the canvas pan/zoom transform).</summary>
    public void Render(DrawingContext context, CanvasRenderContext rc, GateViewViewModel gateView)
    {
        var scene = SceneFor(gateView.Graph, rc.ViewModel);
        var pens = GateViewPens.For(rc.Zoom);
        var viewport = RenderCulling.InflateForCulling(
            RenderCulling.ComputeViewportWorld(rc.ViewModel.PanX, rc.ViewModel.PanY, rc.Bounds, rc.Zoom), rc.Zoom);
        var selected = rc.ViewModel.Selection.SelectedComponents.Select(c => c.Component).ToHashSet();

        foreach (var block in scene.Blocks.Where(b => b.Bounds.Intersects(viewport)))
            DrawBlock(context, rc, block, selected.Contains(block.Component) ? pens.Selected : pens.Block);
        foreach (var route in scene.Routes)
            DrawRoute(context, rc, route, gateView.Levels.LevelOf(route.Net), pens, viewport);
        foreach (var symbol in scene.Symbols.Where(s => s.Footprint.Intersects(viewport)))
            DrawSymbol(context, rc, symbol, selected, pens);
    }

    /// <summary>Lays the scene out again only when the graph changed or a gate moved.</summary>
    internal GateViewScene SceneFor(GateViewGraph graph, DesignCanvasViewModel canvas)
    {
        var fingerprint = Fingerprint(graph, canvas);
        if (ReferenceEquals(graph, _sceneGraph) && fingerprint == _sceneFingerprint)
            return _scene;
        _scene = GateViewSceneBuilder.Build(
            graph, canvas.Components.Select(c => c.Component), ComponentGroupRenderer.CalculateGroupBounds);
        _sceneGraph = graph;
        _sceneFingerprint = fingerprint;
        return _scene;
    }

    /// <summary>A cheap hash of every gate's position — changes whenever a gate or block moves.</summary>
    private static int Fingerprint(GateViewGraph graph, DesignCanvasViewModel canvas)
    {
        var hash = new HashCode();
        hash.Add(canvas.Components.Count);
        foreach (var node in graph.Nodes)
        {
            hash.Add(node.Group.PhysicalX);
            hash.Add(node.Group.PhysicalY);
            if (node.Group.ChildComponents.Count > 0)
                hash.Add(node.Group.ChildComponents[0].PhysicalX + node.Group.ChildComponents[0].PhysicalY);
        }
        foreach (var component in canvas.Components)
            hash.Add(component.X + component.Y);
        return hash.ToHashCode();
    }

    private static void DrawBlock(DrawingContext context, CanvasRenderContext rc, GateViewBlock block, IPen pen)
    {
        context.DrawRectangle(null, pen, block.Bounds);
        GateViewLabelPainter.BlockName(rc, block);
    }

    private static void DrawRoute(
        DrawingContext context, CanvasRenderContext rc, GateNetRoute route, GateNetLevel level, GateViewPens pens, Rect viewport)
    {
        var pen = pens.ForLevel(level);
        var visible = false;
        foreach (var wire in route.Wires)
        {
            if (!BoundsOf(wire).Intersects(viewport))
                continue;
            visible = true;
            for (var i = 1; i < wire.Length; i++)
                context.DrawLine(pen, wire[i - 1], wire[i]);
        }
        var brush = GateViewPens.BrushFor(level);
        foreach (var junction in route.Junctions)
            context.DrawEllipse(brush, null, junction, JunctionRadiusPx * pens.Scale, JunctionRadiusPx * pens.Scale);
        foreach (var flag in route.Flags.Where(f => viewport.Contains(f.Pin)))
        {
            visible = true;
            var end = new Point(flag.Pin.X + (flag.IsOutput ? 1 : -1) * FlagLengthPx * pens.Scale, flag.Pin.Y);
            context.DrawLine(pen, flag.Pin, end);
            GateViewLabelPainter.FlagLabel(rc, route, flag, end, level);
        }
        if (visible && route.Wires.Count > 0)
            GateViewLabelPainter.NetLabel(rc, route, level);
    }

    private static void DrawSymbol(
        DrawingContext context, CanvasRenderContext rc, GateSymbolPlacement symbol, HashSet<Component> selected, GateViewPens pens)
    {
        var node = symbol.Node;
        var isSelected = selected.Contains(node.TopLevelGroup);
        var isHovered = ReferenceEquals(rc.InteractionState.HoveredGroup, node.TopLevelGroup);
        var outline = isSelected ? pens.Selected : isHovered ? pens.Hovered : pens.Symbol;
        var fill = isSelected ? GateViewPens.SelectedFill : GateViewPens.BodyFill;
        if (!GateViewPens.IsDetailed(rc.Zoom))
        {
            // Overview level of detail: a gate is a dot-sized block, its shape would be noise.
            context.DrawRectangle(fill, outline, symbol.Body);
            return;
        }
        GateSymbolPainter.Draw(context, symbol, fill, outline);
        GateViewLabelPainter.SymbolText(context, symbol, rc.Zoom);
        GateViewLabelPainter.GateName(rc, symbol);
    }

    private static Rect BoundsOf(Point[] wire)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in wire)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }
        return new Rect(minX, minY, Math.Max(maxX - minX, 1), Math.Max(maxY - minY, 1));
    }
}
