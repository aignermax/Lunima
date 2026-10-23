using System.Globalization;
using CAP_Core.Components.Core;
using CAP_Core.Components.PinKinds;
using CAP_Core.Export;
using CAP_Core.Routing.InterconnectRouting;

namespace CAP.Avalonia.Services.NazcaExport;

/// <summary>Classifies electrical endpoints and formats material, source-layer, and companion overrides.</summary>
internal static class NazcaSegmentStyle
{
    /// <summary>
    /// The kwargs drawing a segment through a companion xsection: the core's own width (when
    /// the core call names one — otherwise both share Nazca's default) plus <c>xs=…</c>.
    /// </summary>
    internal static string CompanionKwargs(string xsection, ProcessCrossSection crossSection)
    {
        var width = crossSection.WidthMicrometers is double w
            ? $", width={w.ToString("0.0###", CultureInfo.InvariantCulture)}"
            : string.Empty;
        return $"{width}, xs='{xsection}'";
    }

    /// <summary>
    /// The trailing <c>width=…, layer=(…, …)</c> kwargs of a segment call. A source-layer
    /// tag (the GDS layer/datatype the geometry was IMPORTED from, carried by frozen paths
    /// and route-derived connections) wins over the process default: for a metal trace the
    /// tag replaces <see cref="MetalTraceStyle.LayerTuple"/> (the metal WIDTH is kept — the
    /// tag carries no width); for an optical segment it is the only override, so untagged
    /// geometry keeps the Nazca default layer exactly like before. An untagged optical
    /// segment takes the endpoint pins' process stamps (<paramref name="crossSection"/>);
    /// pins without stamps keep the historical bare call (the Nazca/default layer).
    /// </summary>
    internal static string SegmentKwargs(
        MetalTraceStyle? metal, (int Layer, int DataType)? sourceLayer,
        ProcessCrossSection crossSection = default)
    {
        if (metal is not null)
        {
            var layerTuple = sourceLayer is { } tagged
                ? LayerTupleLiteral(tagged)
                : metal.LayerTuple;
            return $", width={metal.WidthLiteral}, layer={layerTuple}";
        }
        if (sourceLayer is { } s)
            return $", layer={LayerTupleLiteral(s)}";

        var ci = CultureInfo.InvariantCulture;
        var width = crossSection.WidthMicrometers is double w
            ? $", width={w.ToString("0.0###", ci)}"
            : string.Empty;
        var layer = crossSection.GdsLayer is int l
            ? $", layer={l.ToString(ci)}"
            : string.Empty;
        return width + layer;
    }

    /// <summary>The <c>(layer, datatype)</c> tuple literal of a source-layer tag.</summary>
    internal static string LayerTupleLiteral((int Layer, int DataType) layer) =>
        $"({layer.Layer.ToString(CultureInfo.InvariantCulture)}, {layer.DataType.ToString(CultureInfo.InvariantCulture)})";

    /// <summary>
    /// True when a connection between these two pins is a metal (electrical) trace: BOTH pins
    /// must be electrical (<see cref="PinKindHelper.IsElectrical(PhysicalPin?)"/>); a mixed
    /// optical+electrical or all-optical connection stays an optical waveguide (issue #686 review).
    /// </summary>
    internal static bool IsMetalConnection(PhysicalPin? first, PhysicalPin? second) =>
        PinKindHelper.IsElectrical(first) && PinKindHelper.IsElectrical(second);
}
