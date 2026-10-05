using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;
using UnitTests.Integration;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the #1307 example <c>Logic Gate Zero Detect 4-bit.lun</c>
/// (rung 5: the photonic zero flag the ISA <c>JZ</c> branches on): renders the shipped
/// file — loaded through the real load path — with the production
/// <c>WaveguideConnectionRenderer</c> and <c>ComponentRenderer</c>
/// (<see cref="Issue1158SceneControl"/>). One overview frame of the whole cascade —
/// OR01 and OR23 in the left column, ORALL in the middle, NOTZ on the right — and one
/// close-up of the junction where the two partial ORs meet. PNGs + manifest.json land
/// in <c>docs/pr-media/issue-1307/</c> when refreshed with <c>CAP_UPDATE_PR_MEDIA=1</c>,
/// otherwise in a temp dir. Same pattern as <see cref="Issue1265And4BitScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1307ZeroDetectScreenshotTests
{
    private const int MinDistinctSampledColors = 4;
    private const int SampleGridSize = 64;

    [AvaloniaFact]
    public async Task CaptureZeroDetectExample()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1307");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);
        var manifest = new List<object>();

        var path = Path.Combine(
            ExampleDesignFilesTests.ExamplesDirectory(), "Logic Gate Zero Detect 4-bit.lun");
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(path);
        var bounds = ComputeSceneBounds(canvas);

        var overview = bounds.Inflate(60);
        Capture(new Issue1158SceneControl(canvas, overview), overview, dir,
            "zerodetect-overview.png", canvasWidthPixels: 2000);
        manifest.Add(new
        {
            file = "zerodetect-overview.png",
            caption = "The shipped 'Logic Gate Zero Detect 4-bit' example (issue #1307): OR01 reads " +
                "A0/A1 and OR23 reads A2/A3 (left column), ORALL combines the two partial ORs (middle), " +
                "NOTZ inverts the result (right) — the flag tap Z is 1 exactly when A0–A3 are all dark, " +
                "the zero flag the ISA JZ branches on.",
        });

        // Close-up on the junction: the two routed wires from the first-stage OR slices
        // meet ORALL's inputs, and ORALL's output runs level into NOTZ.
        var closeup = new Rect(bounds.X - 40, bounds.Y - 40, 1700 + 80, bounds.Height + 80);
        Capture(new Issue1158SceneControl(canvas, closeup), closeup, dir,
            "zerodetect-or-tree-closeup.png", canvasWidthPixels: 1400);
        manifest.Add(new
        {
            file = "zerodetect-or-tree-closeup.png",
            caption = "Close-up of the OR tree: OR01 (top) and OR23 (bottom) are the shipped " +
                "'Logic Gate OR-AND' gate at its OR reading (threshold 0.25); the router carries their " +
                "outputs into ORALL's A and B pins — no overlaps, no blocked wires.",
        });

        ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        manifest.Count.ShouldBe(2);
    }

    private static Rect ComputeSceneBounds(DesignCanvasViewModel canvas)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var comp in canvas.Components)
        {
            minX = Math.Min(minX, comp.X);
            minY = Math.Min(minY, comp.Y);
            maxX = Math.Max(maxX, comp.X + comp.Width);
            maxY = Math.Max(maxY, comp.Y + comp.Height);
        }
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static void Capture(Issue1158SceneControl scene, Rect world, string outputDir,
        string filename, int canvasWidthPixels)
    {
        scene.Width = canvasWidthPixels;
        scene.Height = canvasWidthPixels * world.Height / world.Width;
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
            for (var attempt = 0; attempt < 3 && bitmap == null; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                bitmap = window.CaptureRenderedFrame();
            }
            bitmap.ShouldNotBeNull($"CaptureRenderedFrame stayed null after 3 attempts for {filename}");
            using (bitmap)
            {
                CountDistinctSampledColors(bitmap).ShouldBeGreaterThan(MinDistinctSampledColors,
                    $"Near-blank render for {filename} — likely a missing Skia setup.");
                ScreenshotArtifacts.SavePng(bitmap!, Path.Combine(outputDir, filename));
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
