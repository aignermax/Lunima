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
/// Visual documentation for the #1310 example
/// <c>EBeam Mach-Zehnder Interferometer.lun</c> (openEBL gap #1): renders the
/// shipped file — loaded through the real load path — with the production
/// <c>WaveguideConnectionRenderer</c> and <c>ComponentRenderer</c>
/// (<see cref="Issue1158SceneControl"/>). One overview frame of the full MZI,
/// one close-up of the grating-coupler array (0° orientation, vertical
/// 127 µm pitch). PNGs + manifest.json land in <c>docs/pr-media/issue-1310/</c>
/// when refreshed with <c>CAP_UPDATE_PR_MEDIA=1</c>, otherwise in a temp dir.
/// Same pattern as <see cref="Issue1265And4BitScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class EBeamMziScreenshotTests
{
    private const int MinDistinctSampledColors = 4;
    private const int SampleGridSize = 64;

    [AvaloniaFact]
    public async Task CaptureEBeamMziExample()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1310");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);
        var manifest = new List<object>();

        var path = Path.Combine(
            ExampleDesignFilesTests.ExamplesDirectory(), "EBeam Mach-Zehnder Interferometer.lun");
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(path);
        var bounds = ComputeSceneBounds(canvas);

        var overview = bounds.Inflate(30);
        Capture(new Issue1158SceneControl(canvas, overview), overview, dir,
            "ebeam-mzi-overview.png", canvasWidthPixels: 2000);
        manifest.Add(new
        {
            file = "ebeam-mzi-overview.png",
            caption = "The shipped 'EBeam Mach-Zehnder Interferometer' example (issue #1310): " +
                "built only from the SiEPIC EBeam PDK — two grating couplers (left, 0° orientation, " +
                "vertical 127 µm pitch per openEBL's design-for-test rules), a Y-branch splitter and " +
                "combiner, and the lower arm stretched by a meander for the visible length difference. " +
                "The whole design fits the 605 × 410 µm openEBL die; DRC-lite reports no issues.",
        });

        // Close-up on the grating-coupler column at working zoom: both GCs at 0°,
        // pins facing east into the circuit, exactly 127 µm apart.
        var closeup = new Rect(bounds.X - 20, bounds.Y - 20, 260, bounds.Height + 40);
        Capture(new Issue1158SceneControl(canvas, closeup), closeup, dir,
            "ebeam-mzi-gc-array.png", canvasWidthPixels: 900);
        manifest.Add(new
        {
            file = "ebeam-mzi-gc-array.png",
            caption = "The grating-coupler test array: gc_in (top, laser on) and gc_out (127 µm below, " +
                "laser off — listen-only output), both at 0° orientation on a vertical column — the " +
                "openEBL fiber-array geometry the measurement hardware expects.",
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
