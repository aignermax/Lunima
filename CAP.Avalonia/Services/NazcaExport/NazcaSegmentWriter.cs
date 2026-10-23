using System.Globalization;
using System.Text;
using CAP.Avalonia.Services;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.Routing;
using CAP_Core.Routing.InterconnectRouting;
using static CAP.Avalonia.Services.NazcaExport.NazcaSegmentStyle;

namespace CAP.Avalonia.Services.NazcaExport;

/// <summary>Emits routed straight and bend geometry at absolute Nazca coordinates.</summary>
internal static class NazcaSegmentWriter
{
    /// <summary>
    /// Appends segment-by-segment Nazca export for a routed connection.
    /// Uses absolute .put(x, y, angle) for EVERY segment to avoid coordinate accumulation
    /// errors that occur with Nazca's chaining syntax (.put() without coordinates).
    /// App→Nazca conversion of path geometry is the plain Y negation
    /// (<see cref="NazcaCoordinateMapper.ToNazca"/>); pins live at the same conversion,
    /// so no start-pin offset correction exists — cells are placed so their rendered
    /// pins coincide with the app pins.
    /// </summary>
    /// <param name="sb">Target script builder.</param>
    /// <param name="segments">Routed path segments in editor (app) coordinates.</param>
    /// <param name="startPin">Start pin, used for single-straight pin-to-pin geometry.</param>
    /// <param name="endPin">End pin, used for single-straight pin-to-pin geometry.</param>
    /// <param name="sourceLayer">
    /// Optional GDS layer/datatype tag of the geometry's import source — emitted as
    /// <c>layer=(L, D)</c> on every segment (winning over the metal style's process
    /// default layer, see <see cref="SegmentKwargs"/>). Null keeps the default layers.
    /// </param>
    /// <param name="crossSection">
    /// The endpoint pins' process cross-section: stamped waveguide width/GDS layer are
    /// emitted as <c>width=…, layer=…</c> kwargs on every optical segment, so each
    /// chiplet's routed waveguides land on their own process stack. Callers pass it
    /// on a multi-process canvas (≥2 distinct stamped stacks) and for EBeam-only
    /// designs (<see cref="SiepicEBeamExportProfile"/>); the default (all-null) value
    /// keeps the historical bare calls. The import source-layer tag wins over it
    /// (verbatim round-trip); the metal style ignores it.
    /// </param>
    /// <param name="companionXsection">
    /// Optional Nazca xsection carrying the process companion layers
    /// (<see cref="WaveguideCompanions.NazcaCompanionXsectionPlan"/>): every segment is
    /// emitted a second time through it, at the core's width, so the bands grow beyond the
    /// core along straights and arcs. Null emits the core only.
    /// </param>
    internal static void AppendSegmentExport(
        StringBuilder sb, IReadOnlyList<PathSegment> segments,
        PhysicalPin? startPin = null, PhysicalPin? endPin = null,
        MetalTraceStyle? metal = null, (int Layer, int DataType)? sourceLayer = null,
        ProcessCrossSection crossSection = default, string? companionXsection = null)
    {
        var companionKwargs = companionXsection == null || metal != null
            ? null
            : CompanionKwargs(companionXsection, crossSection);

        // Single straight segment: compute geometry directly from both pin positions
        // so the waveguide hits both pins exactly even if the stored segment drifts.
        if (segments.Count == 1 && segments[0] is StraightSegment && startPin != null && endPin != null)
        {
            sb.AppendLine(FormatStraightSegmentFromPins(startPin, endPin, metal, sourceLayer, crossSection));
            if (companionKwargs != null)
                sb.AppendLine(FormatStraightSegmentFromPins(startPin, endPin, kwargsOverride: companionKwargs));
            return;
        }

        foreach (var segment in segments)
        {
            var (nStartX, nStartY) = NazcaCoordinateMapper.ToNazca(segment.StartPoint.X, segment.StartPoint.Y);
            var (nEndX, nEndY) = NazcaCoordinateMapper.ToNazca(segment.EndPoint.X, segment.EndPoint.Y);

            sb.AppendLine(FormatSegmentAbsolute(segment, nStartX, nStartY, nEndX, nEndY, metal, sourceLayer, crossSection));
            if (companionKwargs != null)
                sb.AppendLine(FormatSegmentAbsolute(segment, nStartX, nStartY, nEndX, nEndY, kwargsOverride: companionKwargs));
        }
    }

    /// <summary>
    /// Formats a path segment (straight or bend) with absolute Nazca positions.
    /// Straight segments compute length and angle from the transformed Nazca endpoints,
    /// ensuring the exported geometry matches the actual endpoint positions.
    /// Bend segments use stored radius/sweep with negated angles for Y-flip.
    /// </summary>
    private static string FormatSegmentAbsolute(
        PathSegment segment, double nazcaStartX, double nazcaStartY,
        double nazcaEndX, double nazcaEndY, MetalTraceStyle? metal = null,
        (int Layer, int DataType)? sourceLayer = null, ProcessCrossSection crossSection = default,
        string? kwargsOverride = null)
    {
        var ci = CultureInfo.InvariantCulture;
        var kwargs = kwargsOverride ?? SegmentKwargs(metal, sourceLayer, crossSection);
        return segment switch
        {
            StraightSegment => FormatStraightAbsolute(
                nazcaStartX, nazcaStartY, nazcaEndX, nazcaEndY, ci, kwargs),
            BendSegment bend => FormatBendAbsolute(bend, nazcaStartX, nazcaStartY, ci, kwargs),
            _ => $"        # Unknown segment type: {segment.GetType().Name}"
        };
    }

    /// <summary>
    /// Formats a straight segment by computing length and angle from Nazca start/end positions.
    /// This is more robust than using stored editor-space angles, because the Nazca Y-flip
    /// is applied to the actual endpoints rather than relying on angle negation.
    /// </summary>
    private static string FormatStraightAbsolute(
        double nazcaStartX, double nazcaStartY,
        double nazcaEndX, double nazcaEndY, CultureInfo ci, string kwargs)
    {
        double dx = nazcaEndX - nazcaStartX;
        double dy = nazcaEndY - nazcaStartY;
        double length = Math.Sqrt(dx * dx + dy * dy);
        double angleDeg = Math.Atan2(dy, dx) * 180.0 / Math.PI;

        var l = length.ToString("F2", ci);
        var x = NazcaCoordinateMapper.NormalizeZero(nazcaStartX).ToString("F2", ci);
        var y = NazcaCoordinateMapper.NormalizeZero(nazcaStartY).ToString("F2", ci);
        var a = NazcaCoordinateMapper.NormalizeZero(angleDeg).ToString("F2", ci);
        return $"        nd.strt(length={l}{kwargs}).put({x}, {y}, {a})";
    }

    /// <summary>
    /// Formats a bend segment with absolute Nazca start position.
    /// The radius is invariant under Y-flip; the sweep angle and start angle are negated.
    /// </summary>
    private static string FormatBendAbsolute(
        BendSegment bend, double nazcaX, double nazcaY, CultureInfo ci, string kwargs)
    {
        var radius = bend.RadiusMicrometers.ToString("F2", ci);
        var sweepAngle = NazcaCoordinateMapper.NormalizeZero(-bend.SweepAngleDegrees).ToString("F2", ci);
        var x = NazcaCoordinateMapper.NormalizeZero(nazcaX).ToString("F2", ci);
        var y = NazcaCoordinateMapper.NormalizeZero(nazcaY).ToString("F2", ci);
        var angle = NazcaCoordinateMapper.NormalizeZero(-bend.StartAngleDegrees).ToString("F2", ci);
        return $"        nd.bend(radius={radius}, angle={sweepAngle}{kwargs}).put({x}, {y}, {angle})";
    }

    /// <summary>
    /// Formats a straight waveguide segment using absolute Nazca pin positions.
    /// Computes length and angle from start pin to end pin in Nazca coordinates,
    /// ensuring the waveguide reaches both pins exactly.
    /// </summary>
    internal static string FormatStraightSegmentFromPins(
        PhysicalPin startPin, PhysicalPin endPin, MetalTraceStyle? metal = null,
        (int Layer, int DataType)? sourceLayer = null, ProcessCrossSection crossSection = default,
        string? kwargsOverride = null)
    {
        var ci = CultureInfo.InvariantCulture;
        var (sx, sy) = NazcaCoordinateMapper.GetPinNazcaPosition(startPin);
        var (ex, ey) = NazcaCoordinateMapper.GetPinNazcaPosition(endPin);

        double dx = ex - sx;
        double dy = ey - sy;
        double length = Math.Sqrt(dx * dx + dy * dy);
        double angleDeg = Math.Atan2(dy, dx) * 180.0 / Math.PI;

        var x = NazcaCoordinateMapper.NormalizeZero(sx).ToString("F2", ci);
        var y = NazcaCoordinateMapper.NormalizeZero(sy).ToString("F2", ci);
        var a = NazcaCoordinateMapper.NormalizeZero(angleDeg).ToString("F2", ci);
        var l = length.ToString("F2", ci);
        var kwargs = kwargsOverride ?? SegmentKwargs(metal, sourceLayer, crossSection);

        return $"        nd.strt(length={l}{kwargs}).put({x}, {y}, {a})";
    }
}
