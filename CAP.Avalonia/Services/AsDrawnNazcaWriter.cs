using System.Globalization;
using System.Text;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Export;

namespace CAP.Avalonia.Services;

/// <summary>
/// Emits <see cref="AsDrawnGeometry"/> into a Nazca export script: every polygon
/// verbatim on its own (layer, datatype), so an untouched import writes back the
/// exact shapes it was read from instead of re-deriving waveguides from the
/// fitted centerline.
/// </summary>
internal static class AsDrawnNazcaWriter
{
    /// <summary>
    /// Coordinate format: three decimals is the 1 nm database grid GDS files are
    /// written on, so the polygons round-trip without loss; trailing zeros are dropped.
    /// </summary>
    private const string CoordinateFormat = "0.###";

    /// <summary>Appends one <c>nd.Polygon</c> line per polygon of <paramref name="geometry"/>.</summary>
    /// <param name="sb">Target script builder.</param>
    /// <param name="geometry">The drawn polygons (absolute canvas µm, Y-down).</param>
    public static void Append(StringBuilder sb, AsDrawnGeometry geometry)
    {
        foreach (var polygon in geometry.Polygons)
        {
            var points = OpenRing(polygon);
            if (points.Count < 3) continue;
            sb.Append("        nd.Polygon(points=[")
              .Append(string.Join(",", points))
              .Append("], layer=(")
              .Append(polygon.Layer.ToString(CultureInfo.InvariantCulture))
              .Append(", ")
              .Append(polygon.DataType.ToString(CultureInfo.InvariantCulture))
              .AppendLine(")).put(0, 0)");
        }
    }

    /// <summary>
    /// The polygon's vertices as Nazca coordinates without consecutive duplicates or the
    /// closing repeat (<c>nd.Polygon</c> closes the ring itself).
    /// </summary>
    private static List<string> OpenRing(OutlinePolygon polygon)
    {
        var ci = CultureInfo.InvariantCulture;
        var points = new List<string>(polygon.Points.Count);
        foreach (var point in polygon.Points)
        {
            var (nx, ny) = NazcaCoordinateMapper.ToNazca(point.X, point.Y);
            var formatted = $"({NazcaCoordinateMapper.NormalizeZero(nx).ToString(CoordinateFormat, ci)}," +
                            $"{NazcaCoordinateMapper.NormalizeZero(ny).ToString(CoordinateFormat, ci)})";
            if (points.Count == 0 || points[^1] != formatted)
                points.Add(formatted);
        }
        if (points.Count > 1 && points[0] == points[^1])
            points.RemoveAt(points.Count - 1);
        return points;
    }
}
