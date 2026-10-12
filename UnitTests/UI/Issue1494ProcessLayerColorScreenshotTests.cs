using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// PR-media renders for issue #1494: the same synthetic CornerStone SiN cell (NITRIDE core
/// on GDS layer 203, HEATER on 39, PAD on 41, a full-cell outline slab) rasterised once
/// with the pre-#1494 demo-PDK layer roles — where the core was the faint "other" grey —
/// and once with the roles derived from the real <c>cornerstone-sin-pdk.json</c> process,
/// where the core carries the waveguide colour and the metals copper. The cspdk Python
/// backend is not required: the geometry is synthetic, only the colouring path is real.
/// </summary>
public class Issue1494ProcessLayerColorScreenshotTests
{
    private const int CoreLayer203 = 203;
    private const int HeaterLayer39 = 39;
    private const int PadLayer41 = 41;
    private const int OutlineLayer999 = 999;

    private const int BitmapWidth = 768;
    private const int BitmapHeight = 160;

    private static string PdkDir =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PDKs");

    [AvaloniaFact]
    public void CornerstoneProcess_ColorsCoreAndMetalLayers()
    {
        var draft = new PdkLoader().LoadFromFile(Path.Combine(PdkDir, "cornerstone-sin-pdk.json"));
        var roles = GdsPreviewLayerRoles.FromProcess(draft.Process);
        var cell = SyntheticCornerstoneCell();

        var before = GdsPolygonRenderer.RasterizeToBitmap(cell, BitmapWidth, BitmapHeight);
        var after = GdsPolygonRenderer.RasterizeToBitmap(cell, BitmapWidth, BitmapHeight, roles);
        before.ShouldNotBeNull();
        after.ShouldNotBeNull();

        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1494");
        Directory.CreateDirectory(dir);
        ScreenshotArtifacts.SavePng(OnDarkBackground(before!), Path.Combine(dir, "cornerstone-preview-before.png"));
        ScreenshotArtifacts.SavePng(OnDarkBackground(after!), Path.Combine(dir, "cornerstone-preview-after.png"));

        // Core band centre: before = faint grey, after = waveguide turquoise (#3CC7B6).
        var coreBefore = PixelAt(before!, 384, 80);
        var coreAfter = PixelAt(after!, 384, 80);
        coreAfter.ShouldNotBe(coreBefore);
        coreAfter.G.ShouldBeGreaterThan((byte)120, "the core must carry the waveguide colour");
        coreAfter.G.ShouldBeGreaterThan((byte)(coreAfter.R + 40), "turquoise is green-dominant");
        Math.Max(coreBefore.R, Math.Max(coreBefore.G, coreBefore.B))
            .ShouldBeLessThan((byte)80, "pre-#1494 the 203 core was the faint 'other' grey");

        // Heater bar centre: after = copper (#C98B4E, warm); before = same faint grey.
        var heaterBefore = PixelAt(before!, 384, 120);
        var heaterAfter = PixelAt(after!, 384, 120);
        heaterAfter.ShouldNotBe(heaterBefore);
        heaterAfter.R.ShouldBeGreaterThan((byte)(heaterAfter.B + 10), "copper is red-warm");
    }

    /// <summary>A CornerStone-flavoured cell: outline slab, heater bar, core waveguide, pad.</summary>
    private static NazcaPreviewResult SyntheticCornerstoneCell() => new()
    {
        Success = true,
        XMin = 0,
        YMin = 0,
        XMax = 100,
        YMax = 20,
        Polygons = new List<NazcaPreviewPolygon>
        {
            Rect(OutlineLayer999, 0, 0, 100, 20),
            Rect(HeaterLayer39, 10, 2, 90, 8),
            Rect(CoreLayer203, 0, 9, 100, 11),
            Rect(PadLayer41, 88, 12, 98, 18),
        },
    };

    private static NazcaPreviewPolygon Rect(int layer, double x0, double y0, double x1, double y1) => new()
    {
        Layer = layer,
        Vertices = new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) },
    };

    /// <summary>Composites the transparent rasterisation over the canvas-dark background.</summary>
    private static RenderTargetBitmap OnDarkBackground(RenderTargetBitmap source)
    {
        var size = source.PixelSize;
        var bitmap = new RenderTargetBitmap(size);
        using var context = bitmap.CreateDrawingContext();
        context.FillRectangle(new SolidColorBrush(Color.Parse("#1A1D26")),
            new Rect(0, 0, size.Width, size.Height));
        context.DrawImage(source, new Rect(0, 0, size.Width, size.Height));
        return bitmap;
    }

    /// <summary>Reads one BGRA (premultiplied) pixel from a rasterised bitmap.</summary>
    private static (byte R, byte G, byte B) PixelAt(RenderTargetBitmap bitmap, int x, int y)
    {
        var region = new PixelRect(x, y, 1, 1);
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(region, buffer, 4, 4);
            return (Marshal.ReadByte(buffer, 2), Marshal.ReadByte(buffer, 1), Marshal.ReadByte(buffer, 0));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
