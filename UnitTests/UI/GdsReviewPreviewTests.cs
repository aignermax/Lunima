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
    public void TextOnlyHierarchyRetainsVisibleAnchor()
    {
        var library = new GdsLibrary();
        var cell = new GdsCell { Name = "TOP" };
        cell.Elements.Add(new GdsText { Layer = 7, Position = new(100, 200), Text = "o1" });
        library.Cells.Add(cell.Name, cell);
        var preview = new GdsReviewPreview { Scene = new GdsReviewSceneBuilder(library).Build("TOP") };
        var window = Open(preview);
        try
        {
            using var frame = new RenderTargetBitmap(new PixelSize(248, 248));
            using (var context = frame.CreateDrawingContext()) preview.Render(context);
            ReadGreen(frame, 124, 124).ShouldBeGreaterThan((byte)100);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CellPickReportsTheClickedAncestorOfAReusedLeaf()
    {
        var library = new GdsLibrary();
        var leaf = new GdsCell { Name = "leaf" };
        leaf.Elements.Add(new GdsPolygon { Layer = 1, Points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)] });
        library.Cells.Add(leaf.Name, leaf);
        foreach (string name in new[] { "A", "B" })
        {
            var parent = new GdsCell { Name = name };
            parent.Elements.Add(new GdsReference { CellName = "leaf" });
            library.Cells.Add(name, parent);
        }
        var top = new GdsCell { Name = "TOP" };
        top.Elements.Add(new GdsReference { CellName = "A" });
        top.Elements.Add(new GdsReference { CellName = "B", Offset = new(100, 0) });
        library.Cells.Add(top.Name, top);
        var preview = new GdsReviewPreview { Scene = new GdsReviewSceneBuilder(library).Build("TOP") };
        IReadOnlyList<string>? picked = null;
        preview.CellPathPicked += path => picked = path;
        var window = Open(preview);
        try
        {
            window.MouseDown(new Point(214, 124), MouseButton.Left);
            window.MouseUp(new Point(214, 124), MouseButton.Left);
            picked.ShouldBe(new[] { "TOP", "B", "leaf" });
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TransformedCellCanBeHighlightedAndPickedInLayoutContext()
    {
        var library = new GdsLibrary();
        var pad = new GdsCell { Name = "pad" };
        pad.Elements.Add(new GdsPolygon { Layer = 9, Points = [new(0, 0), new(10, 0), new(10, 20), new(0, 20)] });
        library.Cells.Add(pad.Name, pad);
        var top = new GdsCell { Name = "TOP" };
        top.Elements.Add(new GdsReference
        {
            CellName = pad.Name,
            Offset = new(100, 200),
            AngleDegrees = 90,
            Reflected = true,
            Magnification = 2
        });
        library.Cells.Add(top.Name, top);
        var preview = new GdsReviewPreview
        {
            Scene = new GdsReviewSceneBuilder(library).Build("TOP"),
            HighlightCellName = "pad"
        };
        string? picked = null;
        preview.CellPicked += name => picked = name;
        var window = Open(preview);
        try
        {
            using var frame = new RenderTargetBitmap(new PixelSize(248, 248));
            using (var context = frame.CreateDrawingContext()) preview.Render(context);
            ReadGreen(frame, 124, 124).ShouldBeGreaterThan((byte)100);
            ReadChannel(frame, 24, 124, 2).ShouldBeGreaterThan((byte)200, "the selected placement has a yellow outline");
            window.MouseDown(new Point(124, 124), MouseButton.Left);
            window.MouseUp(new Point(124, 124), MouseButton.Left);
            picked.ShouldBe("pad");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RepeatedReferenceDagKeepsLaterGeometryVisibleWithoutExpandingEveryPlacement()
    {
        var library = new GdsLibrary();
        var leaf = new GdsCell { Name = "leaf" };
        leaf.Elements.Add(new GdsPolygon { Layer = 1, Points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)] });
        library.Cells.Add(leaf.Name, leaf);
        string child = leaf.Name;
        for (int i = 0; i < 30; i++)
        {
            var cell = new GdsCell { Name = $"level{i}" };
            cell.Elements.Add(new GdsReference { CellName = child });
            cell.Elements.Add(new GdsReference { CellName = child });
            library.Cells.Add(cell.Name, cell);
            child = cell.Name;
        }
        var top = library.Cells[child];
        top.Elements.Add(new GdsPolygon { Layer = 9, Points = [new(40, 40), new(100, 40), new(100, 100), new(40, 100)] });
        var preview = new GdsReviewPreview { Scene = new GdsReviewSceneBuilder(library).Build(child) };
        var window = Open(preview);
        try
        {
            using var frame = new RenderTargetBitmap(new PixelSize(248, 248));
            using (var context = frame.CreateDrawingContext()) preview.Render(context);
            ReadGreen(frame, 164, 84).ShouldBeGreaterThan((byte)100);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void HierarchicalPickingPrefersSmallerDescendantOverParentSlab()
    {
        var library = new GdsLibrary();
        var top = new GdsCell { Name = "TOP" };
        top.Elements.Add(new GdsPolygon { Layer = 1, Points = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)] });
        top.Elements.Add(new GdsReference { CellName = "pad", Offset = new(40, 40) });
        var pad = new GdsCell { Name = "pad" };
        pad.Elements.Add(new GdsPolygon { Layer = 9, Points = [new(0, 0), new(20, 0), new(20, 20), new(0, 20)] });
        library.Cells.Add(top.Name, top);
        library.Cells.Add(pad.Name, pad);
        var preview = new GdsReviewPreview { Scene = new GdsReviewSceneBuilder(library).Build("TOP") };
        GdsLayerPair? picked = null;
        preview.LayerPicked += pair => picked = pair;
        var window = Open(preview);
        try
        {
            window.MouseDown(new Point(124, 124), MouseButton.Left);
            window.MouseUp(new Point(124, 124), MouseButton.Left);
            picked.ShouldBe(new GdsLayerPair(9, 0));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void HugeArrayPreviewKeepsGeometryAfterTheArrayVisibleAndPickable()
    {
        var library = new GdsLibrary();
        var leaf = new GdsCell { Name = "leaf" };
        leaf.Elements.Add(new GdsPolygon { Layer = 1, Points = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)] });
        library.Cells.Add(leaf.Name, leaf);
        var top = new GdsCell { Name = "TOP" };
        top.Elements.Add(new GdsReference
        {
            CellName = "leaf",
            Columns = 1_000_000,
            Rows = 1_000_000,
            ColumnSpacingMicrometers = 2,
            RowSpacingMicrometers = 2
        });
        top.Elements.Add(new GdsPolygon
        {
            Layer = 9,
            Points = [new(2_000_000, 0), new(3_000_000, 0), new(3_000_000, 1_000_000), new(2_000_000, 1_000_000)]
        });
        library.Cells.Add(top.Name, top);
        var preview = new GdsReviewPreview { Scene = new GdsReviewSceneBuilder(library).Build("TOP") };
        GdsLayerPair? picked = null;
        preview.LayerPicked += pair => picked = pair;
        var window = Open(preview);
        try
        {
            using var frame = new RenderTargetBitmap(new PixelSize(248, 248));
            using (var context = frame.CreateDrawingContext()) preview.Render(context);
            ReadGreen(frame, 190, 155).ShouldBeGreaterThan((byte)100);
            window.MouseDown(new Point(190, 155), MouseButton.Left);
            window.MouseUp(new Point(190, 155), MouseButton.Left);
            picked.ShouldBe(new GdsLayerPair(9, 0));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void HierarchicalPreviewRendersAndPicksGeometryBeyondFlattenedBudget()
    {
        var library = new GdsLibrary();
        var cell = new GdsCell { Name = "TOP" };
        library.Cells.Add(cell.Name, cell);
        var small = new GdsPolygon { Layer = 1, Points = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)] };
        cell.Elements.AddRange(Enumerable.Repeat(small, 20001));
        cell.Elements.Add(new GdsPolygon
        {
            Layer = 9,
            Points = [new(40, 40), new(100, 40), new(100, 100), new(40, 100)]
        });
        var preview = new GdsReviewPreview { Scene = new GdsReviewSceneBuilder(library).Build("TOP") };
        GdsLayerPair? picked = null;
        preview.LayerPicked += pair => picked = pair;
        var window = Open(preview);
        try
        {
            using var frame = new RenderTargetBitmap(new PixelSize(248, 248));
            using (var context = frame.CreateDrawingContext()) preview.Render(context);
            ReadGreen(frame, 164, 84).ShouldBeGreaterThan((byte)100);
            window.MouseDown(new Point(164, 84), MouseButton.Left);
            window.MouseUp(new Point(164, 84), MouseButton.Left);
            picked.ShouldBe(new GdsLayerPair(9, 0));
            preview.IsPreviewLimited.ShouldBeFalse();
        }
        finally { window.Close(); }
    }

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
        => ReadChannel(bitmap, x, y, 1);

    private static byte ReadChannel(RenderTargetBitmap bitmap, int x, int y, int channel)
    {
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), buffer, 4, 4);
            return Marshal.ReadByte(buffer, channel);
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
