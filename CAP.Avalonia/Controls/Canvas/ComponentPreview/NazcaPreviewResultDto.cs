// CAP.Avalonia/Controls/Canvas/ComponentPreview/NazcaPreviewResultDto.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CAP_Core.Export;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>
/// JSON-serialisable mirror of <see cref="NazcaPreviewResult"/> for the disk cache.
/// Decouples the on-disk format from the Core type (tuples don't serialise stably).
/// Only the geometry needed for thumbnail rendering is persisted (polygons + bbox).
/// </summary>
public sealed class NazcaPreviewResultDto
{
    public double XMin { get; set; }
    public double YMin { get; set; }
    public double XMax { get; set; }
    public double YMax { get; set; }
    public List<PolygonDto> Polygons { get; set; } = new();

    public sealed class PolygonDto
    {
        public int Layer { get; set; }
        public List<double> Xs { get; set; } = new();
        public List<double> Ys { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Options = new() { IncludeFields = false };

    /// <summary>Serialises the geometry of a successful preview result to JSON.</summary>
    public static string Serialize(NazcaPreviewResult result)
    {
        var dto = new NazcaPreviewResultDto
        {
            XMin = result.XMin, YMin = result.YMin, XMax = result.XMax, YMax = result.YMax,
            Polygons = result.Polygons.Select(p => new PolygonDto
            {
                Layer = p.Layer,
                Xs = p.Vertices.Select(v => v.X).ToList(),
                Ys = p.Vertices.Select(v => v.Y).ToList(),
            }).ToList()
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>Parses JSON back to a <see cref="NazcaPreviewResult"/>; returns null on any error.</summary>
    public static NazcaPreviewResult? Deserialize(string json)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<NazcaPreviewResultDto>(json, Options);
            if (dto == null) return null;
            return new NazcaPreviewResult
            {
                Success = true,
                XMin = dto.XMin, YMin = dto.YMin, XMax = dto.XMax, YMax = dto.YMax,
                Polygons = dto.Polygons.Select(p => new NazcaPreviewPolygon
                {
                    Layer = p.Layer,
                    Vertices = ZipVertices(p.Xs, p.Ys),
                }).ToList()
            };
        }
        catch { return null; }
    }

    private static IReadOnlyList<(double X, double Y)> ZipVertices(List<double> xs, List<double> ys)
    {
        int n = Math.Min(xs.Count, ys.Count);
        var list = new List<(double, double)>(n);
        for (int i = 0; i < n; i++) list.Add((xs[i], ys[i]));
        return list;
    }
}
