using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_DataAccess.Persistence.DTOs;

/// <summary>
/// Persisted form of one <see cref="AsDrawnGeometry"/> polygon. An imported chip
/// carries tens of thousands of polygons with hundreds of thousands of vertices, so
/// the vertices are packed (<see cref="PolygonPointCodec"/>: nanometre integers,
/// delta + zig-zag varint, Base64) instead of one JSON number — let alone one JSON
/// object — per coordinate. Lossless on the 1 nm grid GDS layouts are drawn on.
/// </summary>
public sealed class AsDrawnPolygonDto
{
    /// <summary>GDS layer of the polygon.</summary>
    public int Layer { get; set; }

    /// <summary>GDS datatype of the polygon.</summary>
    public int DataType { get; set; }

    /// <summary>Packed vertex ring (absolute canvas coordinates), see <see cref="PolygonPointCodec"/>.</summary>
    public string Points { get; set; } = "";

    /// <summary>Converts drawn geometry to DTOs; null in, null out.</summary>
    /// <param name="geometry">The geometry to persist, or null.</param>
    public static List<AsDrawnPolygonDto>? FromGeometry(AsDrawnGeometry? geometry) =>
        geometry?.Polygons.Select(p => new AsDrawnPolygonDto
        {
            Layer = p.Layer,
            DataType = p.DataType,
            Points = PolygonPointCodec.Encode(p.Points),
        }).ToList();

    /// <summary>
    /// Restores drawn geometry from DTOs. Returns null for a missing or empty list
    /// (files that predate the field) and skips malformed entries (undecodable or
    /// fewer than three vertices).
    /// </summary>
    /// <param name="dtos">The persisted polygons, or null.</param>
    public static AsDrawnGeometry? ToGeometry(IReadOnlyList<AsDrawnPolygonDto>? dtos) => ToGeometry(dtos, out _);

    /// <summary>
    /// Like <see cref="ToGeometry(IReadOnlyList{AsDrawnPolygonDto}?)"/>, and reports how many
    /// malformed entries were skipped so the loader can say the file is damaged.
    /// </summary>
    /// <param name="dtos">The persisted polygons, or null.</param>
    /// <param name="skipped">Number of entries that could not be decoded.</param>
    public static AsDrawnGeometry? ToGeometry(IReadOnlyList<AsDrawnPolygonDto>? dtos, out int skipped)
    {
        skipped = 0;
        if (dtos is null) return null;
        var polygons = new List<OutlinePolygon>(dtos.Count);
        foreach (var dto in dtos)
        {
            var points = PolygonPointCodec.TryDecode(dto.Points);
            if (points is null || points.Count < 3)
            {
                skipped++;
                continue;
            }
            polygons.Add(new OutlinePolygon { Layer = dto.Layer, DataType = dto.DataType, Points = points });
        }
        return polygons.Count == 0 ? null : new AsDrawnGeometry(polygons);
    }
}
