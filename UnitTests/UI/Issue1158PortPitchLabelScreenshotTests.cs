using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.UI;

/// <summary>
/// Visual walkthrough for issue #1158: (1) a 2x2 MMI's four ports render as four separate
/// Connect-mode markers instead of one merged blob (<see cref="PinPitchSizer"/>), (2) a flat
/// directional coupler's name label sits below its footprint instead of covering the geometry,
/// while (3) taller components keep the classic inside-the-footprint anchor so a dense design
/// (the shipped Full Adder example) doesn't get labels spilling onto neighbours. "Before"
/// frames reproduce the pre-fix constants (full-size markers / in-body label) on top of the
/// same production scene. PNGs + manifest.json go to <c>artifacts/ui-screenshots/issue-1158/</c>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1158PortPitchLabelScreenshotTests
{
    private const int MinDistinctSampledColors = 4;
    private const int SampleGridSize = 64;

    /// <summary>Legacy Connect-mode pin radius (µm) before the pitch-aware shrink.</summary>
    private const double LegacyConnectPinRadius = 8.0;

    [AvaloniaFact]
    public async Task CapturePortPitchAndLabelWalkthrough()
    {
        // Opt-in like UiScreenshotTests: full headless frame captures run only when
        // screenshots are explicitly requested via UI_SHOT_DIR.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return;
        var dir = ResolveOutputDirectory();
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);
        var manifest = new List<object>();

        CaptureMmiScenes(dir, manifest);
        CaptureDirectionalCouplerScenes(dir, manifest);
        await CaptureFullAdderScene(dir, manifest);

        ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>2x2 MMI (20x16 µm, two ports per side 8 µm apart) in Connect mode.</summary>
    private static void CaptureMmiScenes(string dir, List<object> manifest)
    {
        var canvas = new DesignCanvasViewModel();
        var mmi = BuildMmi2x2(x: 140, y: 52);
        canvas.AddComponent(mmi, "MMI 2x2");
        var mainVm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        mainVm.CanvasInteraction.CurrentMode = InteractionMode.Connect;
        // Zoom 3 px/µm: low enough that the screen-space marker cap does not separate the
        // legacy full-size markers by itself — the zoom range where the bug was reported.
        var world = new Rect(50, 7.5, 200, 105);

        Capture(new Issue1158SceneControl(canvas, world)
        {
            MainViewModel = mainVm,
            OverlayWorldDraw = (ctx, zoom) => DrawLegacyConnectMarkers(ctx, mmi, zoom),
        }, world, dir, "01-mmi-connect-before.png", canvasWidthPixels: 600);
        manifest.Add(new
        {
            file = "01-mmi-connect-before.png",
            caption = "Before: the 2x2 MMI's two 8 µm-pitch ports per side render at the "
                + "full Connect-mode marker size and merge into one blob per side "
                + "(reproduction of the pre-fix constants on the same scene).",
        });

        Capture(new Issue1158SceneControl(canvas, world) { MainViewModel = mainVm },
            world, dir, "02-mmi-connect-after.png", canvasWidthPixels: 600);
        manifest.Add(new
        {
            file = "02-mmi-connect-after.png",
            caption = "After: PinPitchSizer shrinks the markers to at most 45% of the port "
                + "pitch, so all four MMI ports are visible as four separate Connect-mode "
                + "markers.",
        });
    }

    /// <summary>Flat 70x7 µm directional coupler whose label used to cover the geometry.</summary>
    private static void CaptureDirectionalCouplerScenes(string dir, List<object> manifest)
    {
        var canvas = new DesignCanvasViewModel();
        var dc = BuildFlatDirectionalCoupler(x: 315, y: 70);
        canvas.AddComponent(dc, "Directional Coupler");
        // Zoom 1.5 px/µm: the screen-clamped label (14 px) towers over the 7 µm (≈10 px)
        // body, so the component classifies as flat — the reported situation.
        var world = new Rect(83, -30, 533, 200);

        Capture(new Issue1158SceneControl(canvas, world)
        {
            SuppressNameLabels = true,
            OverlayWorldDraw = (ctx, zoom) => DrawLegacyInBodyLabel(ctx, dc, zoom),
        }, world, dir, "03-dc-label-before.png", canvasWidthPixels: 800);
        manifest.Add(new
        {
            file = "03-dc-label-before.png",
            caption = "Before: the name label anchored 5 µm inside the top-left corner "
                + "covers the 7 µm tall directional coupler's geometry (reproduction of "
                + "the pre-fix anchor on the same scene).",
        });

        Capture(new Issue1158SceneControl(canvas, world), world, dir,
            "04-dc-label-after.png", canvasWidthPixels: 800);
        manifest.Add(new
        {
            file = "04-dc-label-after.png",
            caption = "After: a component shorter than its own label anchors the name just "
                + "below the footprint — the coupler geometry stays fully visible.",
        });
    }

    /// <summary>The shipped Full Adder example, loaded through the real load path.</summary>
    private static async Task CaptureFullAdderScene(string dir, List<object> manifest)
    {
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), "Logic Gate Full Adder.lun");
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(path);
        var bounds = ComputeSceneBounds(canvas);
        var world = bounds.Inflate(30);

        Capture(new Issue1158SceneControl(canvas, world), world, dir, "05-full-adder-dense.png");
        manifest.Add(new
        {
            file = "05-full-adder-dense.png",
            caption = "Dense check on the shipped Full Adder example (full design): components "
                + "tall enough to host their own label keep the classic inside-the-footprint "
                + "anchor, so no label falls onto neighbouring components or waveguides.",
        });

        // Close-up at working zoom (2 px/µm) on the design's dense top-left region.
        var crop = new Rect(bounds.X - 10, bounds.Y - 10, 600, 300);
        Capture(new Issue1158SceneControl(canvas, crop), crop, dir, "06-full-adder-closeup.png");
        manifest.Add(new
        {
            file = "06-full-adder-closeup.png",
            caption = "Full Adder close-up at working zoom: labels stay inside their own "
                + "(tall) components instead of dropping below the footprint onto the row of "
                + "neighbours beneath.",
        });
    }

    /// <summary>Reproduces the pre-fix Connect-mode markers: unscaled orange circles of the
    /// legacy radius at every pin position, painting over the (smaller) fixed markers.</summary>
    private static void DrawLegacyConnectMarkers(DrawingContext ctx, Component comp, double zoom)
    {
        var brush = new SolidColorBrush(Color.FromRgb(255, 200, 0));
        foreach (var pin in comp.PhysicalPins)
        {
            var (x, y) = pin.GetAbsolutePosition();
            double r = PinScreenSize.CapWorldRadius(LegacyConnectPinRadius, zoom);
            ctx.DrawEllipse(brush, null, new Point(x, y), r, r);
        }
    }

    /// <summary>Reproduces the pre-fix name label: drawn 5 µm inside the top-left corner at
    /// the production font size, straight over the flat component's geometry.</summary>
    private static void DrawLegacyInBodyLabel(DrawingContext ctx, Component comp, double zoom)
    {
        double fontSize = PinScreenSize.ClampWorldFontSize(PinRenderer.NameLabelFontSizeWorld, zoom);
        var text = new FormattedText(comp.HumanReadableName,
            System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Arial"), fontSize, Brushes.White);
        ctx.DrawText(text, new Point(comp.PhysicalX + 5, comp.PhysicalY + 5));
    }

    /// <summary>2x2 MMI stand-in: 20x16 µm body, two optical ports per side 8 µm apart.</summary>
    private static Component BuildMmi2x2(double x, double y) => BuildComponent(
        "MMI 2x2", x, y, width: 20, height: 16,
        new[] { (0.0, 4.0, 180.0), (0.0, 12.0, 180.0), (20.0, 4.0, 0.0), (20.0, 12.0, 0.0) });

    /// <summary>Flat directional-coupler stand-in: 70x7 µm body, one port per corner.</summary>
    private static Component BuildFlatDirectionalCoupler(double x, double y) => BuildComponent(
        "Directional Coupler", x, y, width: 70, height: 7,
        new[] { (0.0, 0.5, 180.0), (0.0, 6.5, 180.0), (70.0, 0.5, 0.0), (70.0, 6.5, 0.0) });

    private static Component BuildComponent(
        string name, double x, double y, double width, double height,
        IReadOnlyList<(double X, double Y, double Angle)> pinOffsets)
    {
        var pins = pinOffsets.Select((p, i) => new PhysicalPin
        {
            Name = $"o{i}",
            OffsetXMicrometers = p.X,
            OffsetYMicrometers = p.Y,
            AngleDegrees = p.Angle,
            LogicalPin = new Pin($"o{i}", i, MatterType.Light, RectSide.Right),
        }).ToList();

        var sMatrix = new SMatrix(new List<Guid>(), new List<(Guid sliderID, double value)>());
        var component = new Component(
            new Dictionary<int, SMatrix> { { 1550, sMatrix } },
            new List<CAP_Core.Components.Core.Slider>(),
            "test", name,
            new Part[1, 1] { { new Part() } },
            -1, $"issue1158_{Guid.NewGuid():N}", new DiscreteRotation(), pins)
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = width,
            HeightMicrometers = height,
            HumanReadableName = name,
        };
        foreach (var pin in pins)
            pin.ParentComponent = component;
        return component;
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
        string filename, int canvasWidthPixels = 1200)
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

    /// <summary>Repo-root <c>artifacts/ui-screenshots/issue-1158</c>, or <c>UI_SHOT_DIR/issue-1158</c>.</summary>
    private static string ResolveOutputDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (!string.IsNullOrEmpty(envDir))
            return Path.Combine(envDir, "issue-1158");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1158");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1158");
    }
}
