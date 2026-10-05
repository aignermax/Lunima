using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for issue #1276 (targeted rip-up-and-reroute for
/// contention-blocked wires): the synthetic two-wire corridor fixture of
/// <c>UnitTests.Routing.ContentionRepairTests</c> rendered through the production
/// <c>WaveguideConnectionRenderer</c> (<see cref="Issue1158SceneControl"/>). The BEFORE
/// frame shows wire B as a red blocked fallback crossing wire A, which occupies the
/// single-file corridor; the AFTER frame shows the accepted repair — B routed through
/// the corridor first, A re-routed around the roof, no red wire, no crossing. PNGs +
/// manifest.json land in <c>docs/pr-media/issue-1276/</c> when refreshed with
/// <c>CAP_UPDATE_PR_MEDIA=1</c>, otherwise in a temp dir. Same pattern as
/// <see cref="Issue1265And4BitScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1276ContentionRepairScreenshotTests
{
    private const int MinDistinctSampledColors = 4;
    private const int SampleGridSize = 64;

    [AvaloniaFact]
    public void CaptureContentionRepairBeforeAfter()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1276");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var canvas = new DesignCanvasViewModel();
        var (connA, connB) = BuildCorridorFixture(canvas);

        connB.Connection.IsBlockedFallback.ShouldBeTrue("the fixture starts with B blocked");
        connB.Connection.FailureReason.ShouldBe(RoutingFailureReason.Contention);

        var world = new Rect(-70, 85, 660, 150);
        using (Capture(new Issue1158SceneControl(canvas, world), world, dir,
                   "contention-repair-before.png", canvasWidthPixels: 1600))
        {
        }

        canvas.ConnectionManager.RepairContentionBlockedWires();

        canvas.ConnectionManager.LastContentionRepairAcceptCount.ShouldBe(1,
            "routing B first and re-routing A behind it unblocks both wires");
        connA.Connection.IsBlockedFallback.ShouldBeFalse();
        connB.Connection.IsBlockedFallback.ShouldBeFalse();

        using (Capture(new Issue1158SceneControl(canvas, world), world, dir,
                   "contention-repair-after.png", canvasWidthPixels: 1600))
        {
        }

        ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(new[]
            {
                new
                {
                    file = "contention-repair-before.png",
                    caption = "Before: wire A (green, through the single-file corridor between roof and " +
                        "floor walls) was routed first; wire B's fallback (red) crosses it — a " +
                        "contention-blocked wire.",
                },
                new
                {
                    file = "contention-repair-after.png",
                    caption = "After the bounded rip-up-and-reroute pass (issue #1276): the pass ripped up " +
                        "wire A, routed B first through the corridor, then re-routed A around the roof. " +
                        "Both wires are clean — the blocked count went from 1 to 0 with no new crossing.",
                },
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// The corridor fixture of <c>UnitTests.Routing.ContentionRepairTests</c>: roof/floor
    /// walls leave a single-file corridor; wire A's clean straight occupies it, wire B's
    /// blocked straight crosses A on the way to its pin on the corridor roof.
    /// </summary>
    private static (WaveguideConnectionViewModel A, WaveguideConnectionViewModel B) BuildCorridorFixture(
        DesignCanvasViewModel canvas)
    {
        var aWest = TestComponentFactory.CreatePinlessComponent(-50, 140, width: 50, height: 40);
        var aEast = TestComponentFactory.CreatePinlessComponent(460, 140, width: 30, height: 40);
        var bEast = TestComponentFactory.CreatePinlessComponent(450, 170, width: 30, height: 40);
        var roof = TestComponentFactory.CreatePinlessComponent(60, 105, width: 280, height: 45);
        var floor = TestComponentFactory.CreatePinlessComponent(60, 170, width: 280, height: 45);

        var pinAStart = AddPin(aWest, 50, 20, 0);
        var pinAEnd = AddPin(aEast, 0, 20, 180);
        var pinBStart = AddPin(bEast, 0, 20, 180);
        var pinBEnd = AddPin(roof, 140, 45, 90);

        canvas.InitializeAStarRouting(-100, -100, 600, 300);
        foreach (var component in new[] { aWest, aEast, bEast, roof, floor })
            canvas.AddComponent(component, "test", "test");

        var connA = canvas.ConnectPinsWithCachedRoute(pinAStart, pinAEnd,
            CreateStraightPath(0, 160, 460, 160, blocked: false))!;
        var connB = canvas.ConnectPinsWithCachedRoute(pinBStart, pinBEnd,
            CreateStraightPath(450, 190, 200, 150, blocked: true))!;
        return (connA, connB);
    }

    private static CAP_Core.Components.Core.PhysicalPin AddPin(
        Component component, double offsetX, double offsetY, double angleDegrees)
    {
        var pin = TestComponentFactory.CreateRoutingPin(component, offsetX, offsetY, angleDegrees);
        component.PhysicalPins.Add(pin);
        return pin;
    }

    private static RoutedPath CreateStraightPath(
        double startX, double startY, double endX, double endY, bool blocked)
    {
        double headingDegrees = Math.Atan2(endY - startY, endX - startX) * 180.0 / Math.PI;
        var path = new RoutedPath { IsBlockedFallback = blocked };
        path.Segments.Add(new StraightSegment(startX, startY, endX, endY, headingDegrees));
        return path;
    }

    private static WriteableBitmap Capture(Issue1158SceneControl scene, Rect world, string outputDir,
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
            CountDistinctSampledColors(bitmap!).ShouldBeGreaterThan(MinDistinctSampledColors,
                $"Near-blank render for {filename} — likely a missing Skia setup.");
            ScreenshotArtifacts.SavePng(bitmap!, Path.Combine(outputDir, filename));
            return bitmap!;
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
        for (int x = 0; x < width; x += stepX)
        {
            for (int y = 0; y < height; y += stepY)
            {
                var rowAddr = fb.Address + y * fb.RowBytes;
                colors.Add(Marshal.ReadInt32(rowAddr, x * 4));
            }
        }
        return colors.Count;
    }
}
