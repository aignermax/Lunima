using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Export;
using Shouldly;
using UnitTests.Integration;
using UnitTests.Services.GdsImport;
using UnitTests.UI;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.Canvas.ComponentPreview;

/// <summary>
/// End-to-end guard for the 0.16.0 invisible-preview regression (#1495): release 0.16.0
/// shipped canvas previews that loaded and drew but stayed invisible (1 px/µm rasterisation
/// plus a grey full-cell slab over the core), and every unit test stayed green because none
/// checked that visible core pixels actually reach the canvas. This journey walks the whole
/// chain: the shipped <c>Mach-Zehnder Interferometer</c> example loads through the real
/// open path (Home → Examples), every component's preview renders through the real
/// <see cref="GdsPreviewRenderService"/> with the real Python renderer, the canvas renders
/// headless (Skia), and the footprints of the Phase Shifter and the 2×2 MMI Coupler must
/// contain a measurable share of waveguide-core-colored pixels. A counter-probe re-rasterises
/// the same geometry with the 0.16.0 styling (<see cref="LegacyPreviewStyle"/>) and must fall
/// below the threshold — proving the assertions would have caught the defect.
/// Nazca-gated: without a nazca-capable Python the test returns early (CI provides one);
/// <c>Skip.If</c> cannot report a skip through AvaloniaFact's discoverer.
/// </summary>
[Trait("Category", "Slow")]
public class CanvasPreviewVisibilityJourneyTests
{
    private const string ExampleFileName = "Mach-Zehnder Interferometer.lun";
    private const string PhaseShifterIdentifier = "mzi_phase_shifter";
    private const string MmiCouplerIdentifier = "mzi_combiner";

    /// <summary>
    /// Minimum share of waveguide-core pixels inside a component footprint. The cores of the
    /// demo-PDK Phase Shifter / 2×2 MMI Coupler cover ~13 % / ~33 % of their bounding boxes
    /// (measured on the captured frame: ~15 % / ~45 % of the inset footprint); the 0.16.0
    /// styling paints them grey, i.e. ~0 %. 2 % separates both by an order of magnitude, so
    /// anti-aliasing and blit resampling can never flip the verdict.
    /// </summary>
    private const double MinWaveguidePixelShare = 0.02;

    /// <summary>Insets the measured footprint so borders, pins and attached routes stay out.</summary>
    private const double FootprintInsetFraction = 0.08;

    private static readonly TimeSpan RenderBudget = TimeSpan.FromSeconds(60);
    private const int PreviewRenderTimeoutSeconds = 30;
    private const int CanvasWidthPixels = 1600;
    private const double WorldMarginMicrometers = 40;

    [AvaloniaFact]
    public async Task LoadedMziExample_ShowsWaveguideCorePixelsInComponentPreviews()
    {
        var python = await GdsUserDesignFixture.FindNazcaPythonAsync();
        if (python == null)
            return; // no nazca engine on this machine — see the class summary for why this is not Skip.If.

        var workDir = Path.Combine(Path.GetTempPath(), "lunima-preview-journey-" + Guid.NewGuid().ToString("N"));
        try
        {
            // ── 1. Load the shipped example through the real Home → Examples path ──
            var canvas = new DesignCanvasViewModel();
            var templates = TestPdkLoader.LoadAllTemplates();
            var fileOps = CreateFileOperations(canvas, templates);
            var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
            (await fileOps.OpenDesignAsCopyAsync(examplePath)).ShouldBeTrue($"'{ExampleFileName}' must open from the Home screen");
            await fileOps.PostLoadRouting;

            // ── 2. Render every preview through the real service + real Python renderer ──
            var previews = CreatePreviewService(python, templates, workDir);
            foreach (var component in canvas.Components)
                previews.TryGetPreview(component.Component); // null while fetching — kicks the render
            var pending = previews.WaitForPendingAsync();
            (await Task.WhenAny(pending, Task.Delay(RenderBudget)) == pending)
                .ShouldBeTrue("all preview renders must finish within the 60 s CI budget");

            var bitmapDeadline = DateTime.UtcNow + RenderBudget;
            foreach (var component in canvas.Components)
            {
                if (!GdsPreviewKey.ForComponent(component.Component).IsRenderable)
                    continue;
                while (previews.TryGetPreview(component.Component)?.Bitmap == null)
                {
                    (DateTime.UtcNow < bitmapDeadline).ShouldBeTrue(
                        $"'{component.Component.Identifier}': rasterised bitmap must arrive within the budget");
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(20);
                }
            }

            // ── 3. Render the canvas headless and persist the PR screenshot ──
            var world = ComputeWorld(canvas);
            using var frame = CaptureFrame(canvas, previews, world);
            var outputDir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1495");
            Directory.CreateDirectory(outputDir);
            ScreenshotArtifacts.SavePng(frame, Path.Combine(outputDir, "mzi-canvas-previews.png"));

            // ── 4. Waveguide-core pixels must be visible in both footprints ──
            var scale = CanvasWidthPixels / world.Width;
            AssertFootprintCoreShare(frame, canvas, world, scale, PhaseShifterIdentifier);
            AssertFootprintCoreShare(frame, canvas, world, scale, MmiCouplerIdentifier);

            // ── 5. Counter-probe: the 0.16.0 styling must FAIL this test's threshold ──
            foreach (var identifier in new[] { PhaseShifterIdentifier, MmiCouplerIdentifier })
            {
                var data = previews.TryGetPreview(FindComponent(canvas, identifier))!;
                using var legacy = LegacyPreviewStyle.Rasterize(
                    data.Result, data.WidthMicrometers, data.HeightMicrometers);
                legacy.ShouldNotBeNull();
                var legacyShare = WaveguidePixelShare(legacy!,
                    new PixelRect(0, 0, legacy!.PixelSize.Width, legacy.PixelSize.Height));
                legacyShare.ShouldBeLessThan(MinWaveguidePixelShare,
                    $"'{identifier}': the 0.16.0 styling ({legacyShare:P2} core pixels) must fail the "
                    + $"threshold — else this journey cannot guard the regression");
            }
        }
        finally
        {
            if (Directory.Exists(workDir))
                Directory.Delete(workDir, true);
        }
    }

    private static void AssertFootprintCoreShare(
        WriteableBitmap frame, DesignCanvasViewModel canvas, Rect world, double scale, string identifier)
    {
        var component = FindComponent(canvas, identifier);
        double inset = FootprintInsetFraction;
        var rect = new PixelRect(
            (int)((component.PhysicalX - world.X + component.WidthMicrometers * inset) * scale),
            (int)((component.PhysicalY - world.Y + component.HeightMicrometers * inset) * scale),
            (int)(component.WidthMicrometers * (1 - 2 * inset) * scale),
            (int)(component.HeightMicrometers * (1 - 2 * inset) * scale));
        var share = WaveguidePixelShare(frame, rect);
        share.ShouldBeGreaterThanOrEqualTo(MinWaveguidePixelShare,
            $"'{identifier}': only {share:P2} waveguide-core pixels in the footprint — "
            + "the preview is invisible like in the 0.16.0 regression");
    }

    private static CAP_Core.Components.Core.Component FindComponent(
        DesignCanvasViewModel canvas, string identifier) =>
        canvas.Components.Single(c => c.Component.Identifier == identifier).Component;

    private static double WaveguidePixelShare(WriteableBitmap frame, PixelRect region) =>
        WaveguidePixelProbe.WaveguidePixelShare(frame, region);

    private static double WaveguidePixelShare(RenderTargetBitmap bitmap, PixelRect region) =>
        WaveguidePixelProbe.WaveguidePixelShare(bitmap, region);

    private static WriteableBitmap CaptureFrame(
        DesignCanvasViewModel canvas, GdsPreviewRenderService previews, Rect world)
    {
        var scene = new CanvasPreviewJourneySceneControl(canvas, previews, world)
        {
            Width = CanvasWidthPixels,
            Height = CanvasWidthPixels * world.Height / world.Width,
        };
        var window = new Window { Width = scene.Width, Height = scene.Height, Content = scene };
        window.Show();
        try
        {
            return HeadlessFrameCapture.Capture(window);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Rect ComputeWorld(DesignCanvasViewModel canvas)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var component in canvas.Components)
        {
            minX = Math.Min(minX, component.Component.PhysicalX);
            minY = Math.Min(minY, component.Component.PhysicalY);
            maxX = Math.Max(maxX, component.Component.PhysicalX + component.Component.WidthMicrometers);
            maxY = Math.Max(maxY, component.Component.PhysicalY + component.Component.HeightMicrometers);
        }
        return new Rect(minX - WorldMarginMicrometers, minY - WorldMarginMicrometers,
            maxX - minX + 2 * WorldMarginMicrometers, maxY - minY + 2 * WorldMarginMicrometers);
    }

    private static GdsPreviewRenderService CreatePreviewService(
        string python, IReadOnlyCollection<ComponentTemplate> templates, string workDir)
    {
        var backend = new NazcaComponentPreviewService(
            python, FindRepoFile(Path.Combine("scripts", "render_component_preview.py")),
            TimeSpan.FromSeconds(PreviewRenderTimeoutSeconds));
        var service = new GdsPreviewRenderService(
            backend, new GdsPreviewDiskCache(Path.Combine(workDir, "disk-cache")));
        service.InlineNazcaCodeLookup = (module, function) => InlineNazcaCode.Find(templates, module, function);
        return service;
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Repo file '{relativePath}' not found above {AppContext.BaseDirectory}");
    }

    private static FileOperationsViewModel CreateFileOperations(
        DesignCanvasViewModel canvas, System.Collections.Generic.List<ComponentTemplate> templates) =>
        new(new CAP_DataAccess.Persistence.NativeDesign.NativeDesignFileStore(),
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            new ObservableCollection<ComponentTemplate>(templates),
            new GdsExportViewModel(new GdsExportService(), new global::CAP_Core.Export.PythonDiscoveryService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!);
}
