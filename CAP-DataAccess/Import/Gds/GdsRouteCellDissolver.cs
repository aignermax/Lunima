namespace CAP_DataAccess.Import.Gds;

/// <summary>
/// Recognizes routed-interconnect cells that our own exporters emit as
/// REFERENCED GDS cells instead of flattened top-cell geometry — nazca with
/// <c>cfg.instantiate_mask_element = True</c> ("straight_N"/"arc_N"…) and the
/// gdsfactory backend's <c>straight</c>/<c>bend_circular</c> factory cells —
/// and dissolves their flattened geometry into the top cell's route polygon
/// sets. The route-connectivity matcher then reconstructs the connection the
/// geometry represents; without dissolution the cell would become a bogus
/// component draft (e.g. "waveguide") and the circuit graph would stay
/// disconnected. Dissolving degrades gracefully: polygons the matcher cannot
/// pair between exactly two pins are imported as frozen route paths, never
/// lost.
/// </summary>
internal static class GdsRouteCellDissolver
{
    /// <summary>
    /// Case-insensitive name prefixes of route/interconnect cells: nazca mask
    /// elements ("straight", "arc", "sinecurve", "cobra", "taper"-free on
    /// purpose — a taper can be a real device), nazca interconnect cells
    /// ("ic_strt", "ic_bend", "ic_sbend"), gdsfactory routing factories
    /// ("straight", "bend", "sbend") and the generic "waveguide" name our own
    /// exports round-trip through. A matching NAME alone never dissolves —
    /// the cell must also be label-free with all geometry on route layers.
    /// </summary>
    private static readonly string[] RouteCellNamePrefixes =
    [
        "waveguide", "straight", "strt", "bend", "sbend", "sinebend",
        "sinecurve", "cobra", "arc", "ic_strt", "ic_bend", "ic_sbend",
    ];

    /// <summary>SiEPIC Waveguide guide layer (1/99) — only waveguide cells carry it.</summary>
    private const int SiepicGuideLayer = 1;
    private const int SiepicGuideDataType = 99;

    /// <summary>SiEPIC PinRec layer (1/10): pin stub paths and opt-pin labels.</summary>
    private const int SiepicPinRecLayer = 1;
    private const int SiepicPinRecDataType = 10;

    /// <summary>SiEPIC DevRec layer (68/0): the waveguide's envelope outline.</summary>
    private const int SiepicDevRecLayer = 68;
    private const int SiepicDevRecDataType = 0;

    /// <summary>
    /// Whether the cell is a routed-interconnect cell that should be dissolved
    /// instead of imported as a component draft. All criteria must hold:
    /// route-style name prefix, at least one polygon, all ROUTE content on the
    /// configured optical/metal route layers, and no text labels anywhere in
    /// the subtree (device cells — including gdsfactory's label-free
    /// <c>stub_*</c> rectangles by name, and every labeled PDK cell by texts —
    /// never qualify).
    /// <para>
    /// Envelope tolerance: gdsfactory emits its route cells with a DEVREC-style
    /// envelope polygon (e.g. layer (68,0)) wrapping the core — structurally an
    /// envelope, not device geometry. Non-route polygons are therefore allowed
    /// when each of their bounding boxes fully CONTAINS the route geometry's
    /// bounding box; any polygon that adds its own geometry outside disqualifies
    /// the cell (real device content).
    /// </para>
    /// <para>
    /// SiEPIC exception: Lunima's own EBeam export wraps each route in a SiEPIC
    /// waveguide cell carrying a Waveguide (1/99) guide, a DevRec (68/0)
    /// envelope, PinRec (1/10) pin stubs and <c>optN</c> pin labels — the labels
    /// and the end-located pin stubs would trip the no-texts and containment
    /// rules above. A cell showing that full signature (guide present, every
    /// text an optN label on PinRec) is route geometry, and its SiEPIC furniture
    /// is exempted from both rules. Real device cells never carry a 1/99 guide.
    /// </para>
    /// </summary>
    public static bool IsRouteCell(
        string cellName, FlattenedGdsCell flattened, GdsHierarchyImportOptions options)
    {
        if (!HasRouteCellName(cellName))
            return false;
        bool siepicWaveguide = IsSiepicWaveguideCell(flattened);
        if ((flattened.Texts.Count > 0 && !siepicWaveguide) || flattened.Polygons.Count == 0)
            return false;

        var routeLayers = new HashSet<(int, int)>(
            options.RouteLayers.Concat(options.MetalRouteLayers));
        var routePolygons = flattened.Polygons
            .Where(p => routeLayers.Contains((p.Layer, p.DataType)))
            .ToList();
        if (routePolygons.Count == 0)
            return false;
        if (routePolygons.Count == flattened.Polygons.Count)
            return true;

        var routeBBox = BoundingUnion(routePolygons);
        const double envelopeToleranceUm = 0.01;
        return flattened.Polygons
            .Where(p => !routeLayers.Contains((p.Layer, p.DataType)))
            .Where(p => !(siepicWaveguide && IsSiepicFurniture(p)))
            .All(p => Contains(BoundingUnion(p), routeBBox, envelopeToleranceUm));
    }

    /// <summary>
    /// Whether the flattened cell shows the SiEPIC waveguide signature our EBeam
    /// export emits: a Waveguide (1/99) guide polygon plus texts that are ALL
    /// <c>optN</c> pin labels on the PinRec layer (1/10). A single foreign label
    /// or a missing guide keeps the strict device-cell rules in force.
    /// </summary>
    private static bool IsSiepicWaveguideCell(FlattenedGdsCell flattened) =>
        flattened.Polygons.Any(p => p.Layer == SiepicGuideLayer && p.DataType == SiepicGuideDataType)
        && flattened.Texts.All(IsSiepicOptPinLabel);

    private static bool IsSiepicOptPinLabel(GdsText text) =>
        text.Layer == SiepicPinRecLayer && text.TextType == SiepicPinRecDataType
        && text.Text.StartsWith("opt", StringComparison.OrdinalIgnoreCase)
        && text.Text.Length > 3 && text.Text.Skip(3).All(char.IsAsciiDigit);

    /// <summary>SiEPIC guide/envelope/pin furniture layers of a waveguide cell —
    /// annotation, not device geometry; regenerated on export.</summary>
    private static bool IsSiepicFurniture(GdsPolygon polygon) =>
        (polygon.Layer, polygon.DataType) is
            (SiepicGuideLayer, SiepicGuideDataType)
            or (SiepicPinRecLayer, SiepicPinRecDataType)
            or (SiepicDevRecLayer, SiepicDevRecDataType);

    /// <summary>
    /// Transforms the cell's flattened ROUTE-LAYER polygons through the
    /// instance's true GDS transform into top-cell app space (Y-down, origin at
    /// the top bbox top-left — the frame the route matcher works in) and appends
    /// them to <paramref name="waveguideSink"/> or <paramref name="metalSink"/>
    /// by layer, so optical and metal networks never merge. Envelope/marker
    /// polygons (non-route layers, see <see cref="IsRouteCell"/>) are skipped —
    /// they wrap the route, they are not part of it.
    /// </summary>
    public static void Dissolve(
        GdsInstance instance,
        FlattenedGdsCell flattened,
        GdsHierarchyImportOptions options,
        GdsBoundingBox topBBox,
        List<GdsOutlinePolygon> waveguideSink,
        List<GdsOutlinePolygon> metalSink)
    {
        var metalLayers = new HashSet<(int, int)>(options.MetalRouteLayers);
        var routeLayers = new HashSet<(int, int)>(
            options.RouteLayers.Concat(options.MetalRouteLayers));
        var transform = GdsInstancePinProjector.TrueTransform(instance);

        foreach (var polygon in flattened.Polygons)
        {
            if (!routeLayers.Contains((polygon.Layer, polygon.DataType)))
                continue;
            var sink = metalLayers.Contains((polygon.Layer, polygon.DataType))
                ? metalSink
                : waveguideSink;
            sink.Add(new GdsOutlinePolygon
            {
                Layer = polygon.Layer,
                DataType = polygon.DataType,
                Points = polygon.Points
                    .Select(point => ToAppSpace(transform.Apply(point), topBBox))
                    .ToList(),
            });
        }
    }

    private static bool HasRouteCellName(string cellName) =>
        RouteCellNamePrefixes.Any(prefix =>
            cellName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static GdsOutlinePoint ToAppSpace(GdsPoint placed, GdsBoundingBox topBBox) =>
        new(placed.X - topBBox.MinX, topBBox.MaxY - placed.Y);

    private static GdsBoundingBox BoundingUnion(GdsPolygon polygon) =>
        BoundingUnion(new[] { polygon });

    private static GdsBoundingBox BoundingUnion(IReadOnlyList<GdsPolygon> polygons)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var polygon in polygons)
        {
            foreach (var point in polygon.Points)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }
        return new GdsBoundingBox(minX, minY, maxX, maxY);
    }

    /// <summary>True when <paramref name="outer"/> contains <paramref name="inner"/>
    /// within <paramref name="toleranceUm"/> on every side.</summary>
    private static bool Contains(GdsBoundingBox outer, GdsBoundingBox inner, double toleranceUm) =>
        outer.MinX <= inner.MinX + toleranceUm && outer.MinY <= inner.MinY + toleranceUm
        && outer.MaxX >= inner.MaxX - toleranceUm && outer.MaxY >= inner.MaxY - toleranceUm;
}
