using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for issue #1398 — canvas badges and register markers on gates
/// nested inside hierarchical cell instances: loads the shipped
/// <c>examples/Logic Gate RAM 2x4.lun</c> through the real load path, builds the
/// network through the real Logic panel, writes 5 into word 0 (A=0, D0+D2, LOAD, one
/// clock commit), and captures the canvas zoomed onto the <c>CELL0</c> word-cell
/// instance so the live 0/1 badges and the "R" markers on its four nested register
/// bits read clearly. PNG + manifest.json land in <c>docs/pr-media/issue-1398/</c>
/// when refreshed with <c>CAP_UPDATE_PR_MEDIA=1</c>, otherwise in a temp dir. Same
/// pattern as <see cref="Issue1389Ram2x4ScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
public class Issue1398Ram2x4BadgeScreenshotTests
{
    private const int MinDistinctSampledColors = 4;
    private const int SampleGridSize = 64;
    private const int CanvasWidthPixels = 1600;
    private const int CaptureAttempts = 3;

    [AvaloniaFact]
    public async Task CaptureRam2x4Cell0Badges()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1398");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var path = Path.Combine(
            ExampleDesignFilesTests.ExamplesDirectory(), "Logic Gate RAM 2x4.lun");
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(path);
        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        var logic = vm.RightPanel.Logic;
        await logic.BuildNetworkCommand.ExecuteAsync(null);
        logic.HasNetwork.ShouldBeTrue(logic.StatusText);

        // Write 5 (D0+D2) into word 0: A=0, LOAD=1, one clock commit — then release
        // the inputs so the capture shows the stored word, not the write stimuli.
        void Set(string pin, bool on) => logic.Inputs.Single(i => i.PinName == pin).IsOn = on;
        Set("D0", true); Set("D2", true); Set("LOAD", true);
        logic.StepClockCommand.Execute(null);
        Set("LOAD", false); Set("D0", false); Set("D2", false);

        var cell0 = canvas.Components.Select(c => c.Component).OfType<ComponentGroup>()
            .Single(g => g.GroupName == "CELL0");
        // Zoom onto the four register-bit gates nested inside CELL0: the union of
        // their group bounds keeps every badge and 'R' marker readable in the capture.
        var registerBounds = cell0.ChildComponents.OfType<ComponentGroup>()
            .Where(g => g.GroupName.StartsWith("REG", StringComparison.Ordinal))
            .Select(ComponentGroupRenderer.CalculateGroupBounds)
            .ToList();
        registerBounds.Count.ShouldBe(4, "CELL0 ships four nested register-bit gates");
        var world = registerBounds.Aggregate((a, b) => a.Union(b)).Inflate(150);

        var sceneHeight = CanvasWidthPixels * world.Height / world.Width;
        var scene = new Issue1158SceneControl(canvas, world)
        {
            OverlayWorldDraw = (context, zoom) =>
            {
                var rc = new CanvasRenderContext
                {
                    ViewModel = canvas,
                    InteractionState = new CanvasInteractionState(),
                    Zoom = zoom,
                    Bounds = new Rect(0, 0, CanvasWidthPixels, sceneHeight),
                };
                LogicGateStateBadgeRenderer.Render(context, rc);
                LogicGateRegisterMarkerRenderer.Render(context, rc);
            },
        };
        Capture(scene, world, dir, "ram2x4-cell0-badges.png");

        ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(new[]
            {
                new
                {
                    file = "ram2x4-cell0-badges.png",
                    caption = "Issue #1398: the RAM 2x4 after writing 5 into word 0, zoomed onto the " +
                        "CELL0 word-cell instance — the four register bits nested inside the cell carry " +
                        "their live 0/1 badges (REG00 and REG02 read 1, the stored 5) and their 'R' " +
                        "register markers exactly like top-level gates.",
                },
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Capture(Issue1158SceneControl scene, Rect world, string outputDir, string filename)
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

        int stepX = Math.Max(1, width / SampleGridSize);
        int stepY = Math.Max(1, height / SampleGridSize);
        var colors = new HashSet<int>();
        for (var y = 0; y < height; y += stepY)
        {
            var rowAddr = fb.Address + y * fb.RowBytes;
            for (var x = 0; x < width; x += stepX)
                colors.Add(Marshal.ReadInt32(rowAddr, x * 4));
        }
        return colors.Count;
    }
}
