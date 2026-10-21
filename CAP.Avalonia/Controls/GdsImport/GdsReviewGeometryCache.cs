using Avalonia;
using Avalonia.Media;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.LayerPicker;

namespace CAP.Avalonia.Controls.GdsImport;

/// <summary>Bounded display geometry, constructed only when the reviewed scene changes.</summary>
internal sealed class GdsReviewGeometryCache
{
    private const int PolygonLimit = 20000;
    private const int VertexLimit = 250000;
    private const int TextLimit = 5000;
    private static readonly string[] Palette = ["#36CFC9", "#819CF4", "#DBA5EC", "#D9B86C", "#86CBA0", "#DD8F87"];
    internal sealed record Layer(GdsLayerPair Pair, IReadOnlyList<Geometry> Geometries, IReadOnlyList<Point> Texts, IBrush Fill);
    internal IReadOnlyList<Layer> Layers { get; }
    internal GdsLayerGeometryScene PickScene { get; }
    internal bool IsLimited { get; }

    internal GdsReviewGeometryCache(GdsLayerGeometryScene scene)
    {
        IsLimited = scene.IsLimited;
        var layers = new List<Layer>();
        var pickLayers = new List<GdsLayerGeometry>();
        var polygonCount = 0;
        var vertexCount = 0;
        var textCount = 0;
        foreach (var layer in scene.Layers)
        {
            var polygons = new List<GdsPolygon>();
            var geometries = new List<Geometry>();
            foreach (var polygon in layer.Polygons)
            {
                if (polygonCount >= PolygonLimit || vertexCount + polygon.Points.Count > VertexLimit)
                {
                    IsLimited = true;
                    continue;
                }
                if (polygon.Points.Count < 3) continue;
                polygonCount++;
                vertexCount += polygon.Points.Count;
                polygons.Add(polygon);
                var geometry = new StreamGeometry();
                using var builder = geometry.Open();
                builder.BeginFigure(Local(polygon.Points[0], scene.Bounds), true);
                foreach (var point in polygon.Points.Skip(1))
                    builder.LineTo(Local(point, scene.Bounds));
                builder.EndFigure(true);
                geometries.Add(geometry);
            }
            var texts = layer.Texts.Take(Math.Max(0, TextLimit - textCount)).ToArray();
            textCount += texts.Length;
            if (texts.Length < layer.Texts.Count) IsLimited = true;
            pickLayers.Add(layer with { Polygons = polygons, Texts = texts });
            layers.Add(new Layer(layer.Pair, geometries, texts.Select(t => Local(t.Position, scene.Bounds)).ToArray(),
                new SolidColorBrush(Color.Parse(Palette[layers.Count % Palette.Length]))));
        }
        Layers = layers;
        PickScene = scene with { Layers = pickLayers };
    }

    private static Point Local(GdsPoint point, GdsBoundingBox bounds) =>
        new(point.X - bounds.MinX, bounds.MaxY - point.Y);
}
