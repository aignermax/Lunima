using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_DataAccess.Persistence.DTOs;

/// <summary>
/// Persisted form of one <see cref="AsDrawnGeometry"/> polygon. The vertices are a
/// flat <c>[x0, y0, x1, y1, …]</c> array (absolute canvas µm): an imported chip
/// carries tens of thousands of polygons, and one JSON object per point would
/// multiply the .lun size for no benefit.
/// </summary>
public sealed class AsDrawnPolygonDto
{
    /// <summary>GDS layer of the polygon.</summary>
    public int Layer { get; set; }

    /// <summary>GDS datatype of the polygon.</summary>
    public int DataType { get; set; }

    /// <summary>Interleaved vertex coordinates (x, y, x, y, …), closed ring.</summary>
    public double[] Xy { get; set; } = Array.Empty<double>();

    /// <summary>Converts drawn geometry to DTOs; null in, null out.</summary>
    /// <param name="geometry">The geometry to persist, or null.</param>
    public static List<AsDrawnPolygonDto>? FromGeometry(AsDrawnGeometry? geometry) =>
        geometry?.Polygons.Select(p => new AsDrawnPolygonDto
        {
            Layer = p.Layer,
            DataType = p.DataType,
            Xy = p.Points.SelectMany(pt => new[] { pt.X, pt.Y }).ToArray(),
        }).ToList();

    /// <summary>
    /// Restores drawn geometry from DTOs. Returns null for a missing or empty list
    /// (files that predate the field) and skips malformed entries with an odd
    /// coordinate count or fewer than three vertices.
    /// </summary>
    /// <param name="dtos">The persisted polygons, or null.</param>
    public static AsDrawnGeometry? ToGeometry(IReadOnlyList<AsDrawnPolygonDto>? dtos)
    {
        if (dtos is null) return null;
        var polygons = new List<OutlinePolygon>(dtos.Count);
        foreach (var dto in dtos)
        {
            if (dto.Xy.Length % 2 != 0 || dto.Xy.Length < 6) continue;
            var points = new OutlinePoint[dto.Xy.Length / 2];
            for (int i = 0; i < points.Length; i++)
                points[i] = new OutlinePoint(dto.Xy[2 * i], dto.Xy[2 * i + 1]);
            polygons.Add(new OutlinePolygon { Layer = dto.Layer, DataType = dto.DataType, Points = points });
        }
        return polygons.Count == 0 ? null : new AsDrawnGeometry(polygons);
    }
}
