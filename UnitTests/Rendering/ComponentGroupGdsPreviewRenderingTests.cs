using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Moq;
using Shouldly;

namespace UnitTests.Rendering;

/// <summary>
/// Pixel-level regression test: a regular (non-imported) component inside a
/// <see cref="ComponentGroup"/> must draw its GDS preview like a top-level component does,
/// not only the plain rectangle body. Same rendering pattern as
/// <see cref="ComponentGroupOutlineRenderingTests"/>.
/// </summary>
public class ComponentGroupGdsPreviewRenderingTests
{
    // World == pixels at zoom 1. Child bbox (20,20)..(80,60); the preview polygon covers
    // only the LEFT half of the footprint, so the right half shows the plain body fill.
    private const double ChildX = 20, ChildY = 20, ChildWidth = 60, ChildHeight = 40;

    private static readonly PixelRect PreviewRegion = new(24, 28, 20, 24);
    private static readonly PixelRect BodyOnlyRegion = new(60, 32, 14, 18);

    [AvaloniaFact]
    public async Task GroupedChild_DrawsGdsPreview_OverItsBody()
    {
        var child = CreateChild();
        var service = await CreateServiceWithCachedGeometry(child);
        var canvas = new DesignCanvasViewModel();
        var group = new ComponentGroup("G");
        group.AddChild(child);
        canvas.AddComponent(group);
        var rc = new CanvasRenderContext
        {
            ViewModel = canvas,
            InteractionState = new CanvasInteractionState(),
            Zoom = 1.0,
            Bounds = new Rect(0, 0, 100, 80),
            GdsPreviewRenderService = service,
        };

        using var bitmap = new RenderTargetBitmap(new PixelSize(100, 80));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            ctx.FillRectangle(Brushes.Black, new Rect(0, 0, 100, 80));
            new ComponentRenderer().Render(ctx, rc);
        }

        // The waveguide layer (100,160,220 @ alpha 180) blends over the body (40,50,70)
        // to a clearly brighter blue; the plain body fill never gets that bright.
        CountPixels(bitmap, PreviewRegion, (r, g, b) => b > 140 && g > 100)
            .ShouldBeGreaterThan(300, "the grouped child must show its GDS geometry");
        CountPixels(bitmap, BodyOnlyRegion, (r, g, b) => b > 140)
            .ShouldBe(0, "outside the polygon only the plain body is drawn");
    }

    private static async Task<GdsPreviewRenderService> CreateServiceWithCachedGeometry(Component child)
    {
        var nazca = new Mock<NazcaComponentPreviewService>("py", "s.py", (TimeSpan?)null, (ProcessLaunchFactory?)null);
        nazca.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LeftHalfPolygon());
        var diskDir = Path.Combine(Path.GetTempPath(), "lunima-group-preview-" + Guid.NewGuid().ToString("N"));
        var service = new GdsPreviewRenderService(nazca.Object, new GdsPreviewDiskCache(diskDir));
        service.TryGetGeometry(GdsPreviewKey.ForComponent(child));
        await service.WaitForPendingAsync();
        return service;
    }

    private static NazcaPreviewResult LeftHalfPolygon() => new()
    {
        Success = true, XMin = 0, YMin = 0, XMax = ChildWidth, YMax = ChildHeight,
        Polygons = new List<NazcaPreviewPolygon>
        {
            new()
            {
                Layer = 1,
                Vertices = new List<(double, double)> { (0, 0), (30, 0), (30, ChildHeight), (0, ChildHeight) },
            },
        },
    };

    private static Component CreateChild() =>
        new(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "demo_mmi",
            nazcaFunctionParams: "",
            parts: new Part[1, 1] { { new Part() } },
            typeNumber: 0,
            identifier: $"child_{Guid.NewGuid():N}",
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: new List<PhysicalPin>())
        {
            PhysicalX = ChildX,
            PhysicalY = ChildY,
            WidthMicrometers = ChildWidth,
            HeightMicrometers = ChildHeight,
        };

    private static int CountPixels(RenderTargetBitmap bitmap, PixelRect region, Func<byte, byte, byte, bool> matches)
    {
        int bufferSize = region.Width * region.Height * 4;
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            bitmap.CopyPixels(region, buffer, bufferSize, region.Width * 4);
            int count = 0;
            for (int i = 0; i < region.Width * region.Height; i++)
            {
                byte blue = Marshal.ReadByte(buffer, i * 4);
                byte green = Marshal.ReadByte(buffer, i * 4 + 1);
                byte red = Marshal.ReadByte(buffer, i * 4 + 2);
                if (matches(red, green, blue))
                    count++;
            }
            return count;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
