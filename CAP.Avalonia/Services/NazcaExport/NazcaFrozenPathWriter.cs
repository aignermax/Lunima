using System.Globalization;
using System.Text;
using CAP.Avalonia.Services;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.Routing;
using CAP_Core.Routing.InterconnectRouting;
using static CAP.Avalonia.Services.NazcaExport.NazcaConnectionFallbackWriter;
using static CAP.Avalonia.Services.NazcaExport.NazcaSegmentStyle;
using static CAP.Avalonia.Services.NazcaExport.NazcaSegmentWriter;

namespace CAP.Avalonia.Services.NazcaExport;

/// <summary>Exports grouped and canvas-level frozen routes while preserving imported polygon outlines.</summary>
internal static class NazcaFrozenPathWriter
{
    /// <summary>
    /// Exports all frozen waveguide paths from a ComponentGroup (and nested groups):
    /// pin-less imported route outlines as verbatim polygons on their original layer
    /// (see <see cref="AppendOutlineRingExport"/> — they hold outline rings, not
    /// centerlines), every pinned path as Nazca segments. A frozen path between two
    /// electrical pins is a metal trace, not an optical waveguide — the same
    /// classification the live connection loop above applies (issue #686
    /// review: this frozen-group path used to call <see cref="AppendSegmentExport"/> without the
    /// metal style at all, so a frozen electrical route always rendered as a waveguide). A
    /// frozen path with placeholder or invalid geometry is left out just like a live
    /// connection — freezing (grouping) a connection must not bypass the export filter. A
    /// frozen path with NO route at all (a connection frozen before it was ever routed keeps
    /// an empty <c>RoutedPath</c>, not null) renders the same pin-to-pin fallback a routeless
    /// live connection gets, instead of silently vanishing. A path carrying the import's
    /// source-layer tag (<see cref="FrozenWaveguidePath.Layer"/>) exports on THAT layer —
    /// manufacturing needs the original layers back, not the process defaults. An EBeam-only
    /// export wraps each optical frozen path in its own SiEPIC waveguide cell
    /// (<see cref="SiepicWaveguideCellWriter"/>) exactly like a routed connection — grouping
    /// must stay transparent to openEBL verification (hierarchy × export seam).
    /// </summary>
    internal static void AppendGroupFrozenPaths(
        StringBuilder sb, ComponentGroup group, MetalTraceStyle metalStyle,
        Dictionary<Component, string> componentNames, List<string>? skippedConnections = null,
        NazcaProcessInterconnectPlan? interconnectPlan = null, bool forceProcessCrossSections = false,
        SiepicWaveguideCellWriter? waveguideCells = null)
    {
        foreach (var frozenPath in group.InternalPaths)
            AppendFrozenPath(sb, frozenPath, metalStyle, componentNames, skippedConnections, interconnectPlan, forceProcessCrossSections, waveguideCells);

        foreach (var child in group.ChildComponents)
        {
            if (child is ComponentGroup nestedGroup)
                AppendGroupFrozenPaths(sb, nestedGroup, metalStyle, componentNames, skippedConnections, interconnectPlan, forceProcessCrossSections, waveguideCells);
        }
    }

    /// <summary>
    /// Exports one frozen waveguide path — shared by group-internal paths and
    /// canvas-level paths (issue #856), so a path released by ungrouping exports
    /// byte-identically to the same path inside its group.
    /// </summary>
    internal static void AppendFrozenPath(
        StringBuilder sb, FrozenWaveguidePath? frozenPath, MetalTraceStyle metalStyle,
        Dictionary<Component, string> componentNames, List<string>? skippedConnections,
        NazcaProcessInterconnectPlan? interconnectPlan = null, bool forceProcessCrossSections = false,
        SiepicWaveguideCellWriter? waveguideCells = null)
    {
        if (frozenPath == null) return;

        // Imported geometry that still carries its drawn polygons exports them verbatim.
        if (frozenPath.AsDrawnGeometry is { } asDrawn)
        {
            AsDrawnNazcaWriter.Append(sb, asDrawn);
            return;
        }

        // A PIN-LESS frozen path holds imported top-cell route geometry: the source
        // polygon's OUTLINE traced as a closed ring of straight segments
        // (GdsImport.GdsFrozenRoutePathFactory.Create) — not a centerline. Emitting the ring
        // edges as waveguides would draw a waveguide along every outline edge (the
        // route polygon becomes two parallel lines, and every re-import multiplies
        // the frozen geometry: 45 rings came back as 2520 polygons in the
        // re-export round-trip test). The honest round-trip is the polygon itself,
        // verbatim on its original layer — exactly what the import read.
        if (frozenPath.StartPin is null
            && frozenPath.Layer is int outlineLayer && frozenPath.DataType is int outlineDataType
            && TryChainOutlineRing(frozenPath.Path?.Segments) is { } ring)
        {
            AppendOutlineRingExport(sb, ring, outlineLayer, outlineDataType);
            return;
        }

        var metal = IsMetalConnection(frozenPath.StartPin, frozenPath.EndPin) ? metalStyle : null;
        // The import source's (layer, datatype) tag: emitted per segment so imported
        // geometry lands back on its original layer (a metal polygon's tag also wins
        // over the process metal layer). Untagged paths keep the historical defaults.
        var sourceLayer = frozenPath.Layer is int layer && frozenPath.DataType is int dataType
            ? (layer, dataType)
            : ((int Layer, int DataType)?)null;
        var segments = frozenPath.Path?.Segments;
        if (segments == null || segments.Count == 0)
        {
            AppendFallbackExport(sb, frozenPath.StartPin, frozenPath.EndPin, componentNames, metal, sourceLayer,
                interconnectPlan?.InterconnectFor(frozenPath) ?? "ic");
            return;
        }

        if (ExportableConnections.TryRecordSkip(
                frozenPath.Path, frozenPath.StartPin, frozenPath.EndPin, skippedConnections))
            return;
        // The frozen path keeps its endpoint pins — frozen together with the geometry at
        // freeze time — so on a multi-process canvas their PDK stamps still carry the
        // chiplet's cross-section; a single-process canvas stays byte-identical to legacy
        // (except EBeam-only designs, which route on the EBeam layer).
        var crossSection = interconnectPlan?.UsesPerProcessCrossSections == true || forceProcessCrossSections
            ? ConnectionCrossSectionResolver.Resolve(frozenPath.StartPin, frozenPath.EndPin)
            : default;
        var companionXs = metal == null && sourceLayer == null
            ? interconnectPlan?.Companions.XsectionFor(frozenPath.StartPin, frozenPath.EndPin, frozenPath.WidthMicrometers)
            : null;
        // An EBeam-only export wraps the optical frozen path in its own SiEPIC
        // waveguide cell — the same treatment routed connections get — so a grouped
        // design passes openEBL verification instead of flattening the frozen route
        // into the top cell ("Shapes outside component" / "Disconnected pin").
        if (metal == null && waveguideCells != null)
        {
            var cellContent = new StringBuilder();
            AppendSegmentExport(cellContent, segments, frozenPath.StartPin, frozenPath.EndPin, metal, sourceLayer, crossSection, companionXs);
            waveguideCells.AppendWaveguideCell(sb, cellContent, segments, frozenPath.StartPin, frozenPath.EndPin);
            return;
        }
        AppendSegmentExport(sb, segments, frozenPath.StartPin, frozenPath.EndPin, metal, sourceLayer, crossSection, companionXs);
    }

    /// <summary>
    /// The closed vertex ring of a pin-less imported frozen path, or null when the
    /// segments are not exactly one closed ring of straight segments (a hand-frozen
    /// connection, a bend-carrying path, or a broken chain) — the caller then keeps
    /// the historical per-segment waveguide emission. The segments of an imported
    /// outline path chain end-to-start exactly (they were traced edge by edge from
    /// the source polygon, see <see cref="GdsImport.GdsFrozenRoutePathFactory.Create"/>), so
    /// exact endpoint equality is the correct test. The returned vertices are in
    /// app-space (Y-down µm), WITHOUT the closing repeat — <c>nd.Polygon</c> closes
    /// the ring itself.
    /// </summary>
    private static List<(double X, double Y)>? TryChainOutlineRing(IReadOnlyList<PathSegment>? segments)
    {
        if (segments is null || segments.Count < 3 || segments.Any(s => s is not StraightSegment))
            return null;

        var ring = new List<(double X, double Y)>(segments.Count) { segments[0].StartPoint };
        foreach (var segment in segments)
        {
            if (segment.StartPoint != ring[^1])
                return null; // discontinuity — not a plain traced ring
            if (segment.EndPoint != ring[^1])
                ring.Add(segment.EndPoint); // skip zero-length edges (source polygon dupes)
        }
        if (ring[^1] != ring[0])
            return null; // open chain — not a polygon outline
        ring.RemoveAt(ring.Count - 1); // the closing repeat
        return ring.Count >= 3 ? ring : null;
    }

    /// <summary>
    /// Emits one imported outline ring as <c>nd.Polygon(points=…, layer=(L, D))</c> on
    /// its original layer — the same verbatim-polygon emission
    /// <see cref="NazcaOutlinePolygonWriter"/> uses for group background outlines,
    /// with the same Y-flip into Nazca's frame every segment export applies.
    /// </summary>
    private static void AppendOutlineRingExport(
        StringBuilder sb, List<(double X, double Y)> ring, int layer, int dataType)
    {
        var ci = CultureInfo.InvariantCulture;
        var points = ring.Select(p =>
        {
            var (nx, ny) = NazcaCoordinateMapper.ToNazca(p.X, p.Y);
            return $"({NazcaCoordinateMapper.NormalizeZero(nx).ToString("F2", ci)},{NazcaCoordinateMapper.NormalizeZero(ny).ToString("F2", ci)})";
        });
        sb.AppendLine($"        nd.Polygon(points=[{string.Join(",", points)}], layer={LayerTupleLiteral((layer, dataType))}).put(0, 0)");
    }
}
