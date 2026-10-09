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
/// Visual documentation for the #1286 example <c>Logic Gate Logic Unit 4-bit.lun</c>
/// (rung 5 step to a real ALU): renders the shipped file — loaded through the real load
/// path — with the production <c>WaveguideConnectionRenderer</c> and
/// <c>ComponentRenderer</c> (<see cref="Issue1158SceneControl"/>). One overview frame of
/// both rows — AND0–AND3 above, NOT0–NOT3 below — and one close-up of bit slice 0
/// (AND0 over NOT0, sharing A0) at working zoom. PNGs + manifest.json land in
/// <c>docs/pr-media/issue-1286/</c> when refreshed with <c>CAP_UPDATE_PR_MEDIA=1</c>,
/// otherwise in a temp dir. Same pattern as
/// <see cref="Issue1265And4BitScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1286LogicUnit4BitScreenshotTests
{
    private const int MinDistinctSampledColors = 4;
    private const int SampleGridSize = 64;
    private const double AndSliceWidth = 1440;

    [AvaloniaFact]
    public async Task CaptureLogicUnit4BitExample()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1286");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);
        var manifest = new List<object>();

        var path = Path.Combine(
            ExampleDesignFilesTests.ExamplesDirectory(), "Logic Gate Logic Unit 4-bit.lun");
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(path);
        var bounds = ComputeSceneBounds(canvas);

        var overview = bounds.Inflate(30);
        Capture(new Issue1158SceneControl(canvas, overview), overview, dir,
            "logicunit4bit-overview.png", canvasWidthPixels: 2000);
        manifest.Add(new
        {
            file = "logicunit4bit-overview.png",
            caption = "The shipped 'Logic Gate Logic Unit 4-bit' example (issue #1286): one chip, two " +
                "ISA operations from the same operands — AND slices AND0–AND3 in the top row, NOT slices " +
                "NOT0–NOT3 below, one column per bit. Inputs A0–A3 (shared by each bit's two slices) and " +
                "B0–B3, outputs Y0–Y3 (A AND B) and N0–N3 (NOT A). No wires join the slices; the operand " +
                "bits arrive through persisted signal names.",
        });

        // Close-up on bit slice 0 at working zoom: the AND gate of the top row and the
        // NOT gate below it both read the shared operand bit A0.
        var closeup = new Rect(bounds.X - 20, bounds.Y - 20, AndSliceWidth + 40, bounds.Height + 40);
        Capture(new Issue1158SceneControl(canvas, closeup), closeup, dir,
            "logicunit4bit-bit-slice-closeup.png", canvasWidthPixels: 1400);
        manifest.Add(new
        {
            file = "logicunit4bit-bit-slice-closeup.png",
            caption = "Close-up of bit slice 0: AND0 (NAND stage feeding an inverter stage, threshold " +
                "0.25) above NOT0 (single-NAND inverter, threshold 0.375) — both read the shared operand " +
                "bit A0; Y0 = A0 AND B0, N0 = NOT A0. The slices stand clear of each other — the " +
                "DesignValidator reports no overlaps.",
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
