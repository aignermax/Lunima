using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Integration;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the issue #1400 word-cell re-floorplan: renders one word
/// cell (<c>CELL0</c>) of the shipped <c>Logic Gate RAM 2x4.lun</c> — loaded through the
/// real load path — cropped to the cell body, so the PR can show the intra-cell routing
/// before (17 of 44 wires on the blocked fallback) and after (9 of 44) the re-floorplan.
/// The PNG lands in <c>docs/pr-media/issue-1400/</c> when refreshed with
/// <c>CAP_UPDATE_PR_MEDIA=1</c>, otherwise in a temp dir. Same pattern as
/// <see cref="Issue1389Ram2x4ScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
public class Issue1400WordCellScreenshotTests
{
    private const int MinDistinctSampledColors = 4;
    private const int SampleGridSize = 64;
    private const int CaptureAttempts = 3;
    private const int CanvasWidthPixels = 2000;
    private const double GateExtentX = 1000;
    private const double GateExtentY = 400;

    [AvaloniaFact]
    public async Task CaptureWordCell()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1400");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(
            ExampleDesignFilesTests.ExamplesDirectory(), "Logic Gate RAM 2x4.lun");
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(path);

        var cell = canvas.Components.Select(c => c.Component).OfType<ComponentGroup>()
            .Single(g => g.GroupName == "CELL0");
        var bounds = CellBounds(cell).Inflate(120);
        Capture(new Issue1158SceneControl(canvas, bounds), bounds, Path.Combine(dir, "word-cell.png"));
    }

    private static Rect CellBounds(ComponentGroup cell)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var gate in cell.ChildComponents)
        {
            minX = Math.Min(minX, gate.PhysicalX);
            minY = Math.Min(minY, gate.PhysicalY);
            maxX = Math.Max(maxX, gate.PhysicalX + GateExtentX);
            maxY = Math.Max(maxY, gate.PhysicalY + GateExtentY);
        }
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static void Capture(Issue1158SceneControl scene, Rect world, string outputPath)
    {
        scene.Width = CanvasWidthPixels;
        scene.Height = CanvasWidthPixels * world.Height / world.Width;
        var window = new Window
        {
            Width = scene.Width,
            Height = scene.Height,
            Content = scene,
            Background = Brushes.Black,
        };
        window.Show();
        try
        {
            WriteableBitmap? bitmap = null;
            for (var attempt = 0; attempt < CaptureAttempts && bitmap == null; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                bitmap = window.CaptureRenderedFrame();
            }
            bitmap.ShouldNotBeNull($"CaptureRenderedFrame stayed null after {CaptureAttempts} attempts");
            using (bitmap)
            {
                CountDistinctSampledColors(bitmap).ShouldBeGreaterThan(MinDistinctSampledColors,
                    "near-blank render — likely a missing Skia setup");
                ScreenshotArtifacts.SavePng(bitmap!, outputPath);
            }
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static int CountDistinctSampledColors(WriteableBitmap bitmap)
    {
        using var fb = bitmap.Lock();
        int width = fb.Size.Width;
        int height = fb.Size.Height;
        if (width <= 0 || height <= 0) return 0;

        int stepX = Math.Max(1, width / SampleGridSize);
        int stepY = Math.Max(1, height / SampleGridSize);
        var colors = new HashSet<int>();
        for (int y = 0; y < height; y += stepY)
        {
            var rowAddr = fb.Address + y * fb.RowBytes;
            for (int x = 0; x < width; x += stepX)
                colors.Add(Marshal.ReadInt32(rowAddr, x * 4));
        }
        return colors.Count;
    }
}
