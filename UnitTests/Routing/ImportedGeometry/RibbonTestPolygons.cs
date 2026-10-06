using CAP_Core.Routing;

namespace UnitTests.Routing.ImportedGeometry;

/// <summary>
/// Builds ribbon outlines the way Nazca/gdsfactory draw waveguides: the centerline
/// offset by ±width/2 on both sides, side A forward then side B backward.
/// </summary>
internal static class RibbonTestPolygons
{
    /// <summary>Outline of a straight ribbon from (x0,y0) to (x1,y1).</summary>
    public static List<(double X, double Y)> Straight(double x0, double y0, double x1, double y1, double width)
    {
        double angle = Math.Atan2(y1 - y0, x1 - x0) * 180.0 / Math.PI;
        return FromCenterline(new[] { (x0, y0, angle), (x1, y1, angle) }, width);
    }

    /// <summary>Outline of the ribbon around <paramref name="bend"/>, with <paramref name="samples"/> points per side.</summary>
    public static List<(double X, double Y)> Arc(BendSegment bend, double width, int samples = 31)
    {
        var centerline = new List<(double X, double Y, double Angle)>();
        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / (samples - 1);
            double tangent = bend.StartAngleDegrees + bend.SweepAngleDegrees * t;
            double radial = (tangent - 90.0 * Math.Sign(bend.SweepAngleDegrees)) * Math.PI / 180.0;
            centerline.Add((bend.Center.X + bend.RadiusMicrometers * Math.Cos(radial),
                            bend.Center.Y + bend.RadiusMicrometers * Math.Sin(radial), tangent));
        }
        return FromCenterline(centerline, width);
    }

    /// <summary>Outline around an arbitrary sampled centerline (point + tangent angle in degrees).</summary>
    public static List<(double X, double Y)> FromCenterline(IReadOnlyList<(double X, double Y, double Angle)> centerline, double width)
    {
        var sideA = new List<(double X, double Y)>();
        var sideB = new List<(double X, double Y)>();
        foreach (var (x, y, angle) in centerline)
        {
            double nx = -Math.Sin(angle * Math.PI / 180.0), ny = Math.Cos(angle * Math.PI / 180.0);
            sideA.Add((x + nx * width / 2, y + ny * width / 2));
            sideB.Add((x - nx * width / 2, y - ny * width / 2));
        }
        sideB.Reverse();
        return sideA.Concat(sideB).ToList();
    }
}
