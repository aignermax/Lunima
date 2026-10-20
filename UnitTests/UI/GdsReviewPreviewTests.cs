using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Runtime.InteropServices;
using CAP.Avalonia.Controls.GdsImport;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.LayerPicker;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>Native pointer, render, and lifecycle coverage for import review.</summary>
public class GdsReviewPreviewTests
{
    [AvaloniaFact]
    public void SceneRendersAndPicksSourceLayerWithNonzeroOrigin()
    {
        var preview = new GdsReviewPreview { Scene = Scene() };
        GdsLayerPair? picked = null;
        preview.LayerPicked += pair => picked = pair;
        var window = Open(preview);
        try
        {
            using var frame = new RenderTargetBitmap(new PixelSize(248, 248));
            using (var context = frame.CreateDrawingContext())
                preview.Render(context);
            ReadGreen(frame, 124, 124).ShouldBeGreaterThan((byte)100,
                "the source polygon must be drawn in the center");
            ReadGreen(frame, 4, 4).ShouldBeLessThan((byte)40,
                "the padded margin must remain the dark canvas background");
            window.MouseDown(new Point(124, 124), MouseButton.Left);
            window.MouseUp(new Point(124, 124), MouseButton.Left);
            picked.ShouldBe(new GdsLayerPair(7, 2));
            preview.HighlightLayer = picked;
            preview.Fit();
            Dispatcher.UIThread.RunJobs();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PortDragUsesLocalCoordinatesAndClampsToBounds()
    {
        var preview = new GdsReviewPreview
        {
            Scene = Scene(),
            ShowPorts = true,
            Ports = [new DetectedPin { XUm = 50, YUm = 50 }]
        };
        var picked = -1;
        (int Index, double X, double Y)? moved = null;
        preview.PortPicked += index => picked = index;
        preview.PortMoved += (index, x, y) => moved = (index, x, y);
        var window = Open(preview);
        try
        {
            window.MouseDown(new Point(124, 124), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(300, -40), RawInputModifiers.LeftMouseButton);
            picked.ShouldBe(0);
            moved.ShouldBe((0, 100d, 0d));
            window.MouseUp(new Point(300, -40), MouseButton.Left);
            moved = null;
            window.MouseMove(new Point(100, 100));
            moved.ShouldBeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SceneChangeCancelsDragAndResetsLimitationNotification()
    {
        var preview = new GdsReviewPreview
        {
            Scene = Scene(),
            ShowPorts = true,
            Ports = [new DetectedPin { XUm = 50, YUm = 50 }]
        };
        var changes = 0;
        preview.PortMoved += (_, _, _) => changes++;
        var window = Open(preview);
        try
        {
            window.MouseDown(new Point(124, 124), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            preview.Scene = null;
            window.MouseMove(new Point(160, 160), RawInputModifiers.LeftMouseButton);
            changes.ShouldBe(0);
            preview.IsPreviewLimited.ShouldBeFalse();
            preview.Scene = Scene();
            window.Content = null;
            window.Content = preview;
            Dispatcher.UIThread.RunJobs();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DisplayCapIsReportedWithoutChangingSource()
    {
        var scene = Scene();
        var polygon = scene.Layers[0].Polygons[0];
        scene = scene with
        {
            Layers = [scene.Layers[0] with { Polygons = Enumerable.Repeat(polygon, 20001).ToArray() }]
        };
        var preview = new GdsReviewPreview { Scene = scene };
        preview.IsPreviewLimited.ShouldBeTrue();
        scene.Layers[0].Polygons.Count.ShouldBe(20001);
        preview.Scene = Scene();
        preview.IsPreviewLimited.ShouldBeFalse();
    }

    private static Window Open(Control preview)
    {
        var window = new Window { Width = 248, Height = 248, Content = preview };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static byte ReadGreen(RenderTargetBitmap bitmap, int x, int y)
    {
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), buffer, 4, 4);
            return Marshal.ReadByte(buffer, 1);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static GdsLayerGeometryScene Scene() => new()
    {
        Bounds = new GdsBoundingBox(100, -200, 200, -100),
        Layers =
        [
            new GdsLayerGeometry
            {
                Pair = new GdsLayerPair(7, 2),
                Polygons =
                [
                    new GdsPolygon
                    {
                        Layer = 7, DataType = 2,
                        Points = [new(100, -200), new(200, -200), new(200, -100), new(100, -100)]
                    }
                ]
            }
        ]
    };
}
