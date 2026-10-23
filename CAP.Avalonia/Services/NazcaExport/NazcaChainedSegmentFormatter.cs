using System.Globalization;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.Routing;

namespace CAP.Avalonia.Services.NazcaExport;

/// <summary>Formats the legacy single-segment API with optional Nazca chaining and pin anchoring.</summary>
internal static class NazcaChainedSegmentFormatter
{
    /// <summary>
    /// Formats a single path segment as a Nazca Python call.
    /// </summary>
    /// <param name="segment">The path segment to format.</param>
    /// <param name="isFirst">If true, includes absolute coordinates; if false, chains with .put().</param>
    /// <param name="startPin">Optional start pin for correct Nazca coordinate calculation (Issue #329 fix)</param>
    internal static string FormatSegment(PathSegment segment, bool isFirst = true, PhysicalPin? startPin = null)
    {
        var ci = CultureInfo.InvariantCulture;

        return segment switch
        {
            StraightSegment straight => FormatStraightSegment(straight, ci, isFirst, startPin),
            BendSegment bend => FormatBendSegment(bend, ci, isFirst, startPin),
            _ => $"        # Unknown segment type: {segment.GetType().Name}"
        };
    }

    private static string FormatStraightSegment(
        StraightSegment straight, CultureInfo ci, bool isFirst, PhysicalPin? startPin = null)
    {
        // For chained segments, use the forward-projected length instead of Euclidean
        // distance. Nazca's nd.strt() goes forward along the propagation direction,
        // so if the segment is slightly diagonal, the Euclidean length would overshoot.
        var length = isFirst
            ? straight.LengthMicrometers
            : ProjectForwardLength(straight);
        var lengthStr = length.ToString("F2", ci);

        if (isFirst)
        {
            double nazcaX;
            double nazcaY;
            if (startPin != null)
            {
                // Anchor the chain on the pin's world position so the waveguide
                // starts exactly where the component's stub pin sits.
                (nazcaX, nazcaY) = NazcaCoordinateMapper.GetPinNazcaPosition(startPin);
            }
            else
            {
                // Without pin info the segment's own start point is the best anchor.
                (nazcaX, nazcaY) = NazcaCoordinateMapper.ToNazca(
                    straight.StartPoint.X, straight.StartPoint.Y);
            }

            var x = NazcaCoordinateMapper.NormalizeZero(nazcaX).ToString("F2", ci);
            var y = NazcaCoordinateMapper.NormalizeZero(nazcaY).ToString("F2", ci);
            var angle = NazcaCoordinateMapper.NormalizeZero(-straight.StartAngleDegrees).ToString("F2", ci);
            return $"        nd.strt(length={lengthStr}).put({x}, {y}, {angle})";
        }

        return $"        nd.strt(length={lengthStr}).put()";
    }

    /// <summary>
    /// Projects a straight segment's length onto its propagation direction.
    /// Nazca's nd.strt(length=L) goes forward by L along the current angle,
    /// so if the segment is slightly diagonal, we need the forward component only.
    /// </summary>
    private static double ProjectForwardLength(StraightSegment straight)
    {
        double dx = straight.EndPoint.X - straight.StartPoint.X;
        double dy = straight.EndPoint.Y - straight.StartPoint.Y;
        double angleRad = straight.StartAngleDegrees * Math.PI / 180.0;
        double projected = dx * Math.Cos(angleRad) + dy * Math.Sin(angleRad);
        return Math.Max(0, projected);
    }

    private static string FormatBendSegment(BendSegment bend, CultureInfo ci, bool isFirst, PhysicalPin? startPin = null)
    {
        var radius = bend.RadiusMicrometers.ToString("F2", ci);
        var sweepAngle = NazcaCoordinateMapper.NormalizeZero(-bend.SweepAngleDegrees).ToString("F2", ci);

        if (isFirst)
        {
            double nazcaX;
            double nazcaY;
            if (startPin != null)
            {
                // Anchor the chain on the pin's world position so the waveguide
                // starts exactly where the component's stub pin sits.
                (nazcaX, nazcaY) = NazcaCoordinateMapper.GetPinNazcaPosition(startPin);
            }
            else
            {
                (nazcaX, nazcaY) = NazcaCoordinateMapper.ToNazca(
                    bend.StartPoint.X, bend.StartPoint.Y);
            }

            var x = NazcaCoordinateMapper.NormalizeZero(nazcaX).ToString("F2", ci);
            var y = NazcaCoordinateMapper.NormalizeZero(nazcaY).ToString("F2", ci);
            var angle = NazcaCoordinateMapper.NormalizeZero(-bend.StartAngleDegrees).ToString("F2", ci);
            return $"        nd.bend(radius={radius}, angle={sweepAngle}).put({x}, {y}, {angle})";
        }

        return $"        nd.bend(radius={radius}, angle={sweepAngle}).put()";
    }
}
