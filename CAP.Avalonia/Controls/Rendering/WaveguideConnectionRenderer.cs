using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.Visualization;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Connections;
using CAP_Core.Components.PinKinds;
using CAP_Core.Routing;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Renders waveguide connections (routed paths with power flow visualization).
/// Skips connections that are internal to component groups and connections whose
/// bounds lie fully outside the (inflated) viewport — see <see cref="RenderCulling"/>.
/// Implements <see cref="ICanvasRenderer"/> for world-space rendering.
/// </summary>
public sealed class WaveguideConnectionRenderer : ICanvasRenderer
{
    /// <summary>Test seam (InternalsVisibleTo UnitTests): connections drawn since the last
    /// <see cref="ResetDrawCounters"/> — the viewport-culling tests assert this against
    /// <see cref="CulledConnectionCount"/>.</summary>
    internal long IssuedConnectionCount { get; private set; }

    /// <summary>Test seam (InternalsVisibleTo UnitTests): connections skipped by the
    /// viewport cull since the last <see cref="ResetDrawCounters"/>.</summary>
    internal long CulledConnectionCount { get; private set; }

    /// <summary>Test seam (InternalsVisibleTo UnitTests): zeroes both counters.</summary>
    internal void ResetDrawCounters() => (IssuedConnectionCount, CulledConnectionCount) = (0, 0);

    private readonly BlockedConnectionMarkerRenderer _blockedMarkers = new();

    /// <summary>Blocked-connection markers collected by the last <see cref="Render"/> (test seam).</summary>
    internal BlockedConnectionMarkerRenderer BlockedMarkers => _blockedMarkers;

    /// <summary>
    /// Draws the warning markers of the blocked connections seen by the last <see cref="Render"/>.
    /// Call after the component pass so pins and bodies can never cover them.
    /// </summary>
    public void RenderBlockedMarkers(DrawingContext context, CanvasRenderContext rc) => _blockedMarkers.Flush(context, rc);

    /// <inheritdoc/>
    public void Render(DrawingContext context, CanvasRenderContext rc)
    {
        var vm = rc.ViewModel;
        // A die's wires live on the assembly canvas (a die is a work surface), so they are
        // drawn like any wire; only ordinary groups hide stale duplicates of their frozen paths.
        var allGroups = WaveguideFilteringHelper.CollectAllGroups(vm.Components.Select(c => c.Component))
            .Where(g => !g.IsDie).ToList();
        bool dimDieWires = vm.Surface.IsContentLocked && vm.Surface.IsActive;
        var cullRect = RenderCulling.InflateForCulling(
            RenderCulling.ComputeViewportWorld(vm.PanX, vm.PanY, rc.Bounds, rc.Zoom), rc.Zoom);

        var hovered = rc.InteractionState.HoveredConnection;
        var lockGroups = LengthLockOverlay.ActiveGroups(vm, hovered);
        foreach (var conn in vm.Connections)
        {
            if (WaveguideFilteringHelper.IsConnectionInternalToAnyGroup(conn.Connection, allGroups))
                continue;

            var segments = conn.Connection.GetPathSegments();
            // Binding validation scans the mutable route's segment lengths. Resolve it
            // once this frame for bounds, pens, geometry and all warning overlays.
            var asDrawn = conn.Connection.AsDrawnGeometry;
            if (!cullRect.Intersects(GetConnectionBounds(conn, segments, asDrawn)))
            {
                CulledConnectionCount++;
                continue;
            }

            IssuedConnectionCount++;
            using (dimDieWires && ViewModels.Canvas.DieSurface.DieWireStyle.IsOnOneDie(conn.Connection)
                       ? context.PushOpacity(ComponentRenderer.LockedDieContentOpacity) : (IDisposable?)null)
                DrawWaveguideConnection(context, conn, vm, ReferenceEquals(conn, hovered), rc, segments, asDrawn, cullRect, _blockedMarkers,
                    lockGroups);
        }

        if (vm.ShowPowerFlow && rc.InteractionState.HoveredConnection != null)
            DrawPowerHoverLabel(context, rc.InteractionState.HoveredConnection, vm);
    }

    /// <summary>
    /// World-space bounds covering everything a connection can paint this frame: the routed
    /// segments (bend circles included, see <see cref="RenderCulling"/>) UNION the straight
    /// endpoint-to-endpoint fallback line, which is what gets drawn when the path is
    /// missing or stale — culling must never drop a visible fallback.
    /// </summary>
    private static Rect GetConnectionBounds(WaveguideConnectionViewModel conn, IReadOnlyList<CAP_Core.Routing.PathSegment> segments, AsDrawnGeometry? asDrawn)
    {
        double minX = Math.Min(conn.StartX, conn.EndX);
        double minY = Math.Min(conn.StartY, conn.EndY);
        var bounds = new Rect(minX, minY,
            Math.Max(conn.StartX, conn.EndX) - minX, Math.Max(conn.StartY, conn.EndY) - minY);
        if (segments.Count > 0)
            bounds = bounds.Union(RenderCulling.ComputeSegmentBounds(segments));
        if (asDrawn is not null)
        {
            var (minX2, minY2, maxX2, maxY2) = asDrawn.Bounds;
            bounds = bounds.Union(new Rect(minX2, minY2, maxX2 - minX2, maxY2 - minY2));
        }
        return bounds;
    }

    private static void DrawWaveguideConnection(DrawingContext context, WaveguideConnectionViewModel conn,
        DesignCanvasViewModel vm, bool isHovered, CanvasRenderContext rc,
        IReadOnlyList<CAP_Core.Routing.PathSegment> segments, AsDrawnGeometry? asDrawn, Rect cullRect,
        BlockedConnectionMarkerRenderer blockedMarkers, IReadOnlySet<double> lockGroups)
    {
        bool hasImportedEndpointMismatch = asDrawn is not null
            && conn.Connection.StartPin is not null && conn.Connection.EndPin is not null
            && !conn.Connection.FrozenPathStillMatchesPins();
        bool isBlocked = conn.IsBlockedFallback || conn.Connection.RoutedPath?.IsInvalidGeometry == true
            || hasImportedEndpointMismatch;
        var pen = SelectPen(conn, vm, isHovered, rc.Pens, isBlocked);
        bool pathIsStale = segments.Count > 0 && UsesStaleFallback(conn) && IsPathStale(segments, conn);
        bool drawsFallbackLine = segments.Count == 0 || pathIsStale;

        if (isBlocked && rc.Zoom >= ComponentRenderer.GroupOverviewZoom)
        {
            blockedMarkers.Collect(conn.Connection.Id, new Point(conn.StartX, conn.StartY), new Point(conn.EndX, conn.EndY),
                drawsFallbackLine ? Array.Empty<CAP_Core.Routing.PathSegment>() : segments);
        }

        if (drawsFallbackLine)
        {
            context.DrawLine(pen, new Point(conn.StartX, conn.StartY), new Point(conn.EndX, conn.EndY));
            return;
        }

        if (asDrawn is not null)
        {
            // Fitted centerlines can diverge from imported masks. Emphasis recolors
            // the cached source geometry without inventing a different visible route.
            bool showsPowerFlow = vm.ShowPowerFlow && vm.PowerFlowVisualizer.CurrentResult != null;
            AsDrawnGeometryRenderer.Draw(context, asDrawn, rc.Zoom, rc.LayerVisibility,
                visibleWorld: cullRect,
                highlightBrush: conn.IsSelected || isHovered || showsPowerFlow ? pen.Brush : null);
            if (hasImportedEndpointMismatch && rc.Zoom >= ComponentRenderer.GroupOverviewZoom)
            {
                // Dashed gaps are warnings, never replacement geometry or exported routes.
                var start = segments[0].StartPoint;
                var end = segments[^1].EndPoint;
                context.DrawLine(rc.Pens.Blocked, new Point(start.X, start.Y), new Point(conn.StartX, conn.StartY));
                context.DrawLine(rc.Pens.Blocked, new Point(end.X, end.Y), new Point(conn.EndX, conn.EndY));
            }
        }
        else
        {
            if (!conn.IsSelected && LengthLockOverlay.GroupOf(conn) is { } group && lockGroups.Contains(group))
                DrawRoute(context, rc.Pens.LengthPeer, conn.Connection.RoutedPath, segments, rc.Zoom);
            DrawRoute(context, pen, conn.Connection.RoutedPath, segments, rc.Zoom);
        }
        DrawConnectionOverlays(context, conn, vm, isHovered, rc.Zoom, rc.Labels, hasImportedEndpointMismatch);
        if (conn.Connection.TargetLengthMicrometers != null && rc.Zoom >= ComponentRenderer.GroupOverviewZoom)
            LengthLockOverlay.DrawBadge(context, segments, rc.Zoom, ViewModels.Canvas.LengthLockActions.IsViolated(conn));
    }

    /// <summary>
    /// Draws whichever of the three independent text overlays apply, each with its own
    /// visibility rule — do not fold these back into a single always-on-or-always-off block:
    /// - Length/loss (or, for an electrical trace, length only): detail info the design already
    ///   conveys via width/thickness/colour, so it is hover/selection-gated (the actual clutter
    ///   reduction this feature targets).
    /// - Power-flow readout (dB/% or "no signal"): a deliberate, already-opt-in visualization
    ///   mode (ShowPowerFlow) whose entire point is to show every connection's power at a
    ///   glance — always-on while active, never hover-gated.
    /// - Manual-style badge (Type != Auto): an at-a-glance signal that the autorouter no longer
    ///   owns this route — always-on, independent of both of the above.
    /// The two plain-text readouts go to the deferred topmost <paramref name="labels"/> queue
    /// so a component body can never paint over them; the badge is drawn inline because it is
    /// a compact style tag anchored to the line itself, not a free-floating label.
    /// </summary>
    private static void DrawConnectionOverlays(DrawingContext context, WaveguideConnectionViewModel conn,
        DesignCanvasViewModel vm, bool isHovered, double zoom, DeferredLabelLayer labels, bool hasImportedEndpointMismatch)
    {
        if (zoom < ComponentRenderer.GroupOverviewZoom)
            return;

        var midX = (conn.StartX + conn.EndX) / 2;
        var midY = (conn.StartY + conn.EndY) / 2;
        bool isElectrical = IsElectricalTrace(conn);

        // Electrical traces carry no optical power flow, so they never show the power-flow
        // readout — their own length-only label takes that slot instead (#682).
        bool showsPowerFlow = !isElectrical && vm.ShowPowerFlow && vm.PowerFlowVisualizer.CurrentResult != null;

        if (hasImportedEndpointMismatch)
        {
            string warning = Services.Localization.LocalizationService.Instance.Translate("Routing.ImportedEndpointMismatch");
            labels.Enqueue(new FormattedText(warning, System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, CanvasTypography.Ui,
                    PinScreenSize.ClampWorldFontSize(LabelFontSizeWorld, zoom), CanvasPalette.DangerBrush),
                CanvasPalette.DangerBrush, new Point(midX, midY - 15));
        }
        else if (showsPowerFlow)
            DrawPowerFlowReadout(labels, conn, vm, midX, midY, zoom);
        else if (ShouldShowLengthLossLabel(isHovered, conn.IsSelected))
            DrawLengthLossLabel(labels, conn, isElectrical, midX, midY, zoom);

        if (conn.Connection.Type != CAP_Core.Components.Connections.WaveguideType.Auto)
            DrawStyleBadge(context, conn, midX, midY, zoom);
    }

    /// <summary>
    /// Whether the length/loss label should be drawn: on demand only (hover or selection), the
    /// only overlay this feature hides by default — extracted as a pure predicate so the gating
    /// rule itself (as opposed to the power-flow readout and style badge, both always-on) is
    /// directly unit-testable.
    /// </summary>
    internal static bool ShouldShowLengthLossLabel(bool isHovered, bool isSelected) => isHovered || isSelected;

    /// <summary>True when both endpoints are electrical pins — i.e. this is a metal trace,
    /// not an optical waveguide (matches the export classification in the exporters).</summary>
    private static bool IsElectricalTrace(WaveguideConnectionViewModel conn) =>
        PinKindHelper.IsElectrical(conn.Connection.StartPin)
        && PinKindHelper.IsElectrical(conn.Connection.EndPin);

    /// <summary>Extra stroke width (world units) added to a power-flow pen under the cursor.</summary>
    private const double HoverThicknessBoost = 2.0;

    /// <summary>
    /// The connection's pen. Selection wins over hover; metal traces keep their copper strip and
    /// never show optical power; with the power-flow view on, the simulated power decides; a
    /// blocked route is danger-dashed; everything else is a plain waveguide.
    /// </summary>
    private static IPen SelectPen(WaveguideConnectionViewModel conn, DesignCanvasViewModel vm, bool isHovered, CanvasPens pens, bool isBlocked)
    {
        if (conn.IsSelected)
            return pens.WaveguideSelected;

        if (IsElectricalTrace(conn))
            return isHovered ? pens.ElectricalHover : pens.Electrical;

        if (vm.ShowPowerFlow && vm.PowerFlowVisualizer.CurrentResult != null)
        {
            var flow = vm.PowerFlowVisualizer.GetFlowForConnection(conn.Connection.Id);
            return flow != null
                ? Emphasize(PowerFlowRenderer.CreatePowerPen(flow, vm.PowerFlowVisualizer.FadeThresholdDb), isHovered)
                : pens.NoFlow;
        }

        if (isBlocked)
            return isHovered ? pens.BlockedHover : pens.Blocked;

        return isHovered ? pens.WaveguideHover : pens.Waveguide;
    }

    /// <summary>
    /// Emphasizes a hovered power-flow connection so the user sees it is clickable: keeps the
    /// pen's colour intent but draws it thicker and brighter. Returns the pen unchanged when not hovered.
    /// </summary>
    private static IPen Emphasize(IPen basePen, bool isHovered)
    {
        if (!isHovered)
            return basePen;

        var brush = basePen.Brush is ISolidColorBrush solid
            ? new SolidColorBrush(CanvasPalette.Lighten(solid.Color, 0.5))
            : basePen.Brush;

        return new Pen(brush, basePen.Thickness + HoverThicknessBoost)
        {
            DashStyle = basePen.DashStyle,
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
    }

    /// <summary>
    /// Only automatic, non-frozen routes may be replaced by the straight endpoint-to-endpoint
    /// fallback when their endpoints drift (<see cref="IsPathStale"/>). Styled (non-Auto) and
    /// frozen routes always draw their REAL geometry: their curve is the truth the user chose
    /// and the exporter writes — e.g. an honest Straight between non-collinear pins visibly
    /// stops short of the end pin instead of being faked into a diagonal line.
    /// </summary>
    private static bool UsesStaleFallback(WaveguideConnectionViewModel conn) =>
        conn.Connection.Type == CAP_Core.Components.Connections.WaveguideType.Auto
        && !conn.Connection.IsRouteFrozen;

    private static bool IsPathStale(IReadOnlyList<CAP_Core.Routing.PathSegment> segments, WaveguideConnectionViewModel conn)
    {
        var first = segments[0];
        var last = segments[^1];
        double startDist = Math.Sqrt(Math.Pow(first.StartPoint.X - conn.StartX, 2) + Math.Pow(first.StartPoint.Y - conn.StartY, 2));
        double endDist = Math.Sqrt(Math.Pow(last.EndPoint.X - conn.EndX, 2) + Math.Pow(last.EndPoint.Y - conn.EndY, 2));
        return startDist > 1.0 || endDist > 1.0;
    }

    /// <summary>
    /// Draws the route as one geometry, cached per route and zoom level (see
    /// <see cref="RouteGeometryCache"/>) — one draw call instead of one per arc chord.
    /// </summary>
    private static void DrawRoute(DrawingContext context, IPen pen, CAP_Core.Routing.RoutedPath? path,
        IReadOnlyList<CAP_Core.Routing.PathSegment> segments, double zoom)
    {
        var geometry = path != null && ReferenceEquals(path.Segments, segments)
            ? RouteGeometryCache.For(path, zoom)
            : RouteGeometryCache.Build(segments, 0);
        context.DrawGeometry(null, pen, geometry);
    }

    /// <summary>World-space font size for the length/loss label, before the screen-space clamp.</summary>
    private const double LabelFontSizeWorld = 10.0;

    /// <summary>World-space font size for the manual-style badge, before the screen-space clamp.</summary>
    private const double BadgeFontSizeWorld = 9.0;

    /// <summary>Hover/selection-gated length/loss label — see <see cref="DrawConnectionOverlays"/>
    /// for why this is the only overlay of the three that is ever hidden.</summary>
    private static void DrawLengthLossLabel(DeferredLabelLayer labels, WaveguideConnectionViewModel conn,
        bool isElectrical, double midX, double midY, double zoom)
    {
        // Metal traces carry no optical loss figure — label them with their length only, in the
        // electrical copper/gold tint (#682).
        string labelText = isElectrical ? $"{conn.PathLength:F0}µm" : $"{conn.PathLength:F0}µm, {conn.LossDb:F2}dB";
        IBrush labelBrush = isElectrical ? CanvasPalette.CopperBrush : CanvasPalette.TextPrimaryBrush;

        double fontSize = PinScreenSize.ClampWorldFontSize(LabelFontSizeWorld, zoom);
        labels.Enqueue(
            new FormattedText(labelText, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, CanvasTypography.Ui, fontSize, labelBrush),
            labelBrush,
            new Point(midX, midY - 15));
    }

    /// <summary>Always-on power-flow readout (dB/% or "no signal") while <c>ShowPowerFlow</c> is
    /// active — see <see cref="DrawConnectionOverlays"/> for why hover never gates this.</summary>
    private static void DrawPowerFlowReadout(DeferredLabelLayer labels, WaveguideConnectionViewModel conn,
        DesignCanvasViewModel vm, double midX, double midY, double zoom)
    {
        var flow = vm.PowerFlowVisualizer.GetFlowForConnection(conn.Connection.Id);
        string labelText;
        IBrush labelBrush;
        if (flow != null && flow.AveragePower > 0)
        {
            labelText = $"{flow.NormalizedPowerDb:F1}dB ({flow.NormalizedPowerFraction * 100:F0}%)";
            var fraction = Math.Clamp(flow.NormalizedPowerFraction, 0, 1);
            labelBrush = new SolidColorBrush(PowerFlowRenderer.InterpolatePowerColor(fraction));
        }
        else
        {
            labelText = "no signal";
            labelBrush = CanvasPalette.TextTertiaryBrush;
        }

        double fontSize = PinScreenSize.ClampWorldFontSize(LabelFontSizeWorld, zoom);
        labels.Enqueue(
            new FormattedText(labelText, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, CanvasTypography.Ui, fontSize, labelBrush),
            labelBrush,
            new Point(midX, midY - 15));
    }

    private static readonly IBrush StyleBadgeBrush = CanvasPalette.Brush(CanvasPalette.AccentHover);

    /// <summary>Always-on badge for a manually styled connection (Type != Auto): shows the Nazca
    /// style name above the label so it's obvious at a glance that the autorouter no longer owns
    /// this route — see <see cref="DrawConnectionOverlays"/> for why hover never gates this.
    /// Style names are Nazca terms and stay untranslated, like the picker entries.</summary>
    private static void DrawStyleBadge(DrawingContext context, WaveguideConnectionViewModel conn,
        double midX, double midY, double zoom)
    {
        double fontSize = PinScreenSize.ClampWorldFontSize(BadgeFontSizeWorld, zoom);
        context.DrawText(
            new FormattedText(conn.Connection.Type.ToString(),
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                CanvasTypography.UiSemiBold, fontSize, StyleBadgeBrush),
            new Point(midX, midY - 28));
    }

    private static void DrawPowerHoverLabel(DrawingContext context, WaveguideConnectionViewModel conn, DesignCanvasViewModel vm)
    {
        var flow = vm.PowerFlowVisualizer.GetFlowForConnection(conn.Connection.Id);
        if (flow == null) return;
        PowerFlowRenderer.DrawPowerLabel(context, flow, new Point((conn.StartX + conn.EndX) / 2, (conn.StartY + conn.EndY) / 2 - 25));
    }
}
