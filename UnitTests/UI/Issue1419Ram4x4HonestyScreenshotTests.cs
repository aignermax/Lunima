using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for #1419 (RAM examples honesty): renders the re-baked shipped
/// <c>Logic Gate RAM 4x4.lun</c> — loaded through the real load path — as one canvas
/// overview (<see cref="Issue1158SceneControl"/>). The re-bake froze the blocked
/// intra-cell wires with their flag, and the load pipeline now restores the chip size
/// before classifying restored blocked wires, so the design checks no longer invent
/// "pin sealed by a component footprint" findings. PNG + manifest.json land in
/// <c>docs/pr-media/issue-1419/</c> when refreshed with <c>CAP_UPDATE_PR_MEDIA=1</c>,
/// otherwise in a temp dir. Slim variant of <see cref="Issue1408Ram4x4ScreenshotTests"/>:
/// overview only, the write/read demo already lives there.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1419Ram4x4HonestyScreenshotTests
{
    private const int MinDistinctSampledColors = 4;
    private const int CaptureAttempts = 3;

    [AvaloniaFact]
    public async Task CaptureRam4x4Overview()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1419");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var path = Path.Combine(
            ExampleDesignFilesTests.ExamplesDirectory(), "Logic Gate RAM 4x4.lun");
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(path);

        var bounds = ComputeSceneBounds(canvas).Inflate(60);
        Capture(new Issue1158SceneControl(canvas, bounds), bounds, dir,
            "ram4x4-overview.png", canvasWidthPixels: 2000);

        var manifest = new[]
        {
            new
            {
                file = "ram4x4-overview.png",
                caption = "The re-baked 'Logic Gate RAM 4x4' example (issue #1419): the blocked intra-cell " +
                    "wires of the four word-cell instances are frozen with their flag, and the design checks " +
                    "report honest contention reasons instead of the startup-grid artifact that masqueraded " +
                    "as footprint-sealed pins.",
            },
        };
        ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
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
            for (var attempt = 0; attempt < CaptureAttempts && bitmap == null; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                bitmap = window.CaptureRenderedFrame();
            }
            bitmap.ShouldNotBeNull($"CaptureRenderedFrame stayed null after {CaptureAttempts} attempts for {filename}");
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

        int stepX = Math.Max(1, width / 64);
        int stepY = Math.Max(1, height / 64);
        var colors = new HashSet<int>();
        for (int y = 0; y < height; y += stepY)
        {
            var rowAddr = fb.Address + y * fb.RowBytes;
            for (int x = 0; x < width; x += stepX)
                colors.Add(System.Runtime.InteropServices.Marshal.ReadInt32(rowAddr, x * 4));
        }
        return colors.Count;
    }
}
