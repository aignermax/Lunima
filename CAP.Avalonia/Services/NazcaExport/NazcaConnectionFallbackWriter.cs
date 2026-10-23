using System.Globalization;
using System.Text;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using static CAP.Avalonia.Services.NazcaExport.NazcaSegmentWriter;

namespace CAP.Avalonia.Services.NazcaExport;

/// <summary>Emits pin-to-pin optical or electrical fallback geometry for connections without routes.</summary>
internal static class NazcaConnectionFallbackWriter
{
    /// <summary>
    /// Appends the pin-to-pin fallback for a connection/frozen path with no routed geometry
    /// (null or empty). Shared by live connections and zero-segment frozen paths (a group can
    /// freeze a connection that was never routed) so both render identically instead of a
    /// frozen path silently vanishing where a live one would fall back.
    /// </summary>
    internal static void AppendFallbackExport(
        StringBuilder sb,
        PhysicalPin? startPin,
        PhysicalPin? endPin,
        Dictionary<Component, string> componentNames,
        MetalTraceStyle? metal = null,
        (int Layer, int DataType)? sourceLayer = null,
        string interconnect = "ic")
    {
        if (startPin == null || endPin == null)
            return;

        // A routeless electrical connection is a direct metal straight between both pins on the
        // metal layer — the optical sbend interconnect (ic) would draw it as a waveguide (#682).
        if (metal != null)
        {
            sb.AppendLine(FormatStraightSegmentFromPins(startPin, endPin, metal, sourceLayer));
            return;
        }

        var startRef = BuildEndpointReference(startPin, componentNames);
        var endRef = BuildEndpointReference(endPin, componentNames);

        if (startRef != null && endRef != null)
            sb.AppendLine($"        {interconnect}.sbend_p2p({startRef}, {endRef}).put()");
    }

    /// <summary>
    /// Builds the Nazca expression anchoring one connection endpoint for the p2p fallback.
    /// A PDK cell defines its own pin names which generally do NOT match the in-app names
    /// (KeyError at script run time), so its endpoint is anchored by absolute Nazca position
    /// and direction.
    /// </summary>
    private static string? BuildEndpointReference(
        PhysicalPin pin,
        Dictionary<Component, string> componentNames)
    {
        var component = pin.ParentComponent;
        if (component == null || !componentNames.TryGetValue(component, out _))
            return null;

        var ci = CultureInfo.InvariantCulture;
        var (x, y) = NazcaCoordinateMapper.GetPinNazcaPosition(pin);
        var px = x.ToString("F2", ci);
        var py = y.ToString("F2", ci);
        var pa = NazcaCoordinateMapper.GetPinNazcaAngle(pin).ToString("F0", ci);
        return $"({px}, {py}, {pa})";
    }
}
