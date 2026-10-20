using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Controls.GdsImport;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.LayerPicker;
using System.Diagnostics;
using Xunit.Abstractions;

namespace UnitTests.UI;

public class GdsReviewRenderBenchmarkTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task LocalOptInRenderBenchmark()
    {
        var path = Environment.GetEnvironmentVariable("GDS_REVIEW_RENDER_PROBE");
        if (string.IsNullOrEmpty(path)) return;
        await using var input = File.OpenRead(path);
        var library = await new GdsReader().ReadAsync(input);
        var referenced = library.Cells.Values.SelectMany(c => c.Elements.OfType<GdsReference>()).Select(r => r.CellName).ToHashSet();
        var top = library.Cells.Keys.First(n => !referenced.Contains(n));
        foreach (var suggestion in GdsReviewRouteEvidence.Suggest(library, top))
            output.WriteLine($"route candidate {suggestion.Layer}/{suggestion.Datatype}: {suggestion.ShapeCount} source shapes in {suggestion.CellCount} cells");
        var scene = new GdsReviewSceneBuilder(library).Build(top);
        using var cache = new GdsReviewHierarchyCache(scene);
        var scale = Math.Min(1000 / scene.Bounds.Width, 700 / scene.Bounds.Height);
        var matrix = new Matrix(scale, 0, 0, scale, -scene.Bounds.MinX * scale, -scene.Bounds.MinY * scale);
        using var frame = new RenderTargetBitmap(new PixelSize(1000, 700));
        var denseLayer = library.Cells.Values.SelectMany(c => c.Elements.OfType<GdsPolygon>()).GroupBy(p => new GdsLayerPair(p.Layer, p.DataType)).OrderByDescending(g => g.Sum(p => (long)p.Points.Count)).First().Key;
        foreach (var selected in new GdsLayerPair?[] { null, denseLayer })
        foreach (var detail in new[] { 0, 1, 2 })
        foreach (var zoom in new[] { 1d, 10d, 100d })
        {
            var view = matrix * Matrix.CreateTranslation(-500, -350) * Matrix.CreateScale(zoom, zoom) * Matrix.CreateTranslation(500, 350);
            var times = new List<double>();
            for (int i = 0; i < 6; i++)
            {
                var timer = Stopwatch.StartNew();
                using (var context = frame.CreateDrawingContext()) cache.Render(context, view, new Rect(0, 0, 1000, 700), selected, null, true, detail);
                times.Add(timer.Elapsed.TotalMilliseconds);
            }
            output.WriteLine($"selected={selected} detail={detail} zoom={zoom} cold={times[0]:F2}ms warm={times.Skip(1).Average():F2}ms {cache.LastRenderStats}");
        }
    }
}
