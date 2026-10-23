using CAP_DataAccess.Import.Gds;

namespace UnitTests.Import.Gds;

/// <summary>Hand-built detector geometry uses micrometers in Y-up GDS space.</summary>
internal static class GdsPinDetectorFixture
{
    internal const double Tolerance = 1e-9;

    internal static readonly GdsBoundingBox Box10x4 = new(0, 0, 10, 4);

    internal static FlattenedGdsCell Cell(params GdsElement[] elements)
    {
        var cell = new FlattenedGdsCell { CellName = "TEST" };
        foreach (var element in elements)
        {
            switch (element)
            {
                case GdsPolygon polygon:
                    cell.Polygons.Add(polygon);
                    break;
                case GdsText text:
                    cell.Texts.Add(text);
                    break;
            }
        }
        return cell;
    }

    internal static GdsPolygon Poly(int layer, int dataType, params (double X, double Y)[] points) =>
        new()
        {
            Layer = layer,
            DataType = dataType,
            Points = points.Select(p => new GdsPoint(p.X, p.Y)).ToList(),
        };

    /// <summary>
    /// A tessellated annulus sector (gdsfactory bend_circular shape): outer arc from
    /// <paramref name="fromDegrees"/> to <paramref name="toDegrees"/>, exit face, inner
    /// arc back, entry face, closed. Sweep direction follows the angle sign.
    /// </summary>
    internal static GdsPolygon AnnulusSector(int layer, int dataType, double centerX, double centerY,
        double radiusOuter, double radiusInner, double fromDegrees, double toDegrees, int segments = 48)
    {
        var points = new List<GdsPoint>();
        for (int i = 0; i <= segments; i++)
        {
            double phi = (fromDegrees + ((toDegrees - fromDegrees) * i / segments)) * Math.PI / 180.0;
            points.Add(new GdsPoint(centerX + (radiusOuter * Math.Cos(phi)), centerY + (radiusOuter * Math.Sin(phi))));
        }
        for (int i = segments; i >= 0; i--)
        {
            double phi = (fromDegrees + ((toDegrees - fromDegrees) * i / segments)) * Math.PI / 180.0;
            points.Add(new GdsPoint(centerX + (radiusInner * Math.Cos(phi)), centerY + (radiusInner * Math.Sin(phi))));
        }
        points.Add(points[0]);
        return new GdsPolygon
        {
            Layer = layer,
            DataType = dataType,
            Points = points,
        };
    }

    internal static GdsText Label(int layer, int textType, string text, double x, double y) =>
        new()
        {
            Layer = layer,
            TextType = textType,
            Text = text,
            Position = new GdsPoint(x, y),
        };

    /// <summary>
    /// A nazca-style pin-arrow chevron: 7 vertices, tip at (tipX, tipY), body
    /// extending bodyDirX along x (~0.35 × 0.5 µm).
    /// </summary>
    internal static GdsPolygon Chevron(int layer, double tipX, double tipY, int bodyDirX)
    {
        (double X, double Y)[] shape =
            { (0.25, -0.25), (0, 0), (0.25, 0.25), (0.25, 0.125), (0.35, 0.125), (0.35, -0.125), (0.25, -0.125) };
        return new GdsPolygon
        {
            Layer = layer,
            DataType = 0,
            Points = shape.Select(p => new GdsPoint(tipX + bodyDirX * p.X, tipY + p.Y)).ToList(),
        };
    }
}
