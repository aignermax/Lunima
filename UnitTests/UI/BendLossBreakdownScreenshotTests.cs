using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Controls.HelpAnimations;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.Views.Panels;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the radius-dependent bend-loss model: the Properties-panel
/// "Insertion loss" section for a connection with two tight 5 µm bends (propagation / bends /
/// junctions / total plus one row per bend), and the (?) flyout's light-leaks-out-of-a-tight-bend
/// animation at its three <see cref="BendLeakAnimation.ShowcasePhases"/>. PNGs + manifest.json
/// land in <c>docs/pr-media/bend-loss/</c> with <c>CAP_UPDATE_PR_MEDIA=1</c>. Same pattern as
/// <see cref="Issue1256LengthMatchHelpScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class BendLossBreakdownScreenshotTests
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    private sealed record ManifestEntry(string File, string Caption);

    [AvaloniaFact]
    public async Task CaptureLossBreakdownPanelAndHelpAnimation()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("bend-loss");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var manifest = new List<ManifestEntry>();
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");

        var canvas = new DesignCanvasViewModel();
        var connection = AddTightSRoute(canvas);
        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        vm.BottomPanel.ConnectionLoss.SelectedConnection = connection;
        vm.BottomPanel.ConnectionLoss.HasConnection.ShouldBeTrue();

        var panel = new ConnectionLossPanel { DataContext = vm };
        var window = new Window { Width = 520, Height = 760, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            using (Capture(window, dir, "bend-loss-1-properties-breakdown.png",
                "Properties panel, insertion loss of the selected connection: propagation, bends (radius dependent) and junctions itemized, one row per bend.",
                manifest))
            {
            }

            var help = panel.GetVisualDescendants().OfType<HelpFlyoutButton>().FirstOrDefault();
            help.ShouldNotBeNull("the loss section must carry the (?) help button");
            var innerButton = help.GetVisualDescendants().OfType<Button>().First();
            innerButton.Flyout!.ShowAt(innerButton);
            await SettleFlyout();

            var animations = window.GetVisualDescendants().OfType<BendLeakAnimation>().ToList();
            animations.ShouldNotBeEmpty("the flyout must contain the light-leak animation");
            foreach (var animation in animations)
                animation.AutoPlay = false;

            const string caption = "Insertion-loss help: two pulses enter a tight and a gentle 90° bend; light leaks out of the tight one (red) and the readouts are the real model losses.";
            SetProgress(animations, BendLeakAnimation.ShowcasePhases[0]);
            var first = Capture(window, dir, "bend-loss-2-help-approach.png", caption + " (approaching)", manifest);
            SetProgress(animations, BendLeakAnimation.ShowcasePhases[1]);
            var leak = Capture(window, dir, "bend-loss-3-help-leak.png", caption + " (light leaks out of the tight bend)", manifest);
            SetProgress(animations, BendLeakAnimation.ShowcasePhases[2]);
            using (Capture(window, dir, "bend-loss-4-help-arrived.png", caption + " (the tight-bend pulse arrives dimmer)", manifest))
            {
            }

            using (first)
            using (leak)
                CountDifferingPixels(first, leak).ShouldBeGreaterThan(0, "the animation must animate");
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            LocalizationService.Instance.SetLanguage(previousLanguage);
            System.Globalization.CultureInfo.CurrentCulture = previousCulture;
        }

        ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        manifest.Count.ShouldBe(4);
    }

    /// <summary>
    /// Two facing waveguides offset by 60 µm, joined by an S-route of two 5 µm arcs
    /// (start pin (250,125) → end pin (400,185)).
    /// </summary>
    private static WaveguideConnectionViewModel AddTightSRoute(DesignCanvasViewModel canvas)
    {
        var startComp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        startComp.WidthMicrometers = startComp.HeightMicrometers = 250;
        var endComp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        endComp.WidthMicrometers = endComp.HeightMicrometers = 250;
        endComp.PhysicalX = 400;
        endComp.PhysicalY = 60;
        canvas.AddComponent(startComp);
        canvas.AddComponent(endComp);

        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(250, 125, 300, 125, 0));
        path.Segments.Add(new BendSegment(300, 130, 5, 0, 90));
        path.Segments.Add(new StraightSegment(305, 130, 305, 180, 90));
        path.Segments.Add(new BendSegment(310, 180, 5, 90, -90));
        path.Segments.Add(new StraightSegment(310, 185, 400, 185, 0));

        var connection = canvas.ConnectPinsWithCachedRoute(
            startComp.PhysicalPins.First(p => p.Name == "out"), endComp.PhysicalPins.First(p => p.Name == "in"), path);
        connection.ShouldNotBeNull();
        return connection!;
    }

    private static async Task SettleFlyout()
    {
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(600);
        for (int i = 0; i < 30; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(10);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void SetProgress(IEnumerable<HelpAnimationBase> animations, double progress)
    {
        foreach (var animation in animations)
            animation.Progress = progress;
        PumpRenderLoop();
    }

    private static WriteableBitmap Capture(
        Window window, string dir, string filename, string caption, List<ManifestEntry> manifest)
    {
        PumpRenderLoop();
        var bitmap = window.CaptureRenderedFrame();
        bitmap.ShouldNotBeNull($"CaptureRenderedFrame returned null for {filename}");
        SamplePixels(bitmap).ToHashSet().Count.ShouldBeGreaterThan(MinDistinctSampledColors,
            $"Near-blank render in {filename}.");
        ScreenshotArtifacts.SavePng(bitmap, Path.Combine(dir, filename));
        manifest.Add(new ManifestEntry(filename, caption));
        return bitmap;
    }

    private static void PumpRenderLoop()
    {
        for (int i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static int CountDifferingPixels(WriteableBitmap a, WriteableBitmap b)
    {
        using var fa = a.Lock();
        using var fb = b.Lock();
        int differing = 0;
        for (int y = 0; y < Math.Min(fa.Size.Height, fb.Size.Height); y++)
        {
            var rowA = fa.Address + y * fa.RowBytes;
            var rowB = fb.Address + y * fb.RowBytes;
            for (int x = 0; x < Math.Min(fa.Size.Width, fb.Size.Width); x++)
                if (Marshal.ReadInt32(rowA, x * 4) != Marshal.ReadInt32(rowB, x * 4))
                    differing++;
        }
        return differing;
    }

    private static List<int> SamplePixels(WriteableBitmap bitmap)
    {
        using var fb = bitmap.Lock();
        var pixels = new List<int>();
        int stepX = Math.Max(1, fb.Size.Width / SampleGridSize);
        int stepY = Math.Max(1, fb.Size.Height / SampleGridSize);
        for (int y = 0; y < fb.Size.Height; y += stepY)
        {
            var rowAddr = fb.Address + y * fb.RowBytes;
            for (int x = 0; x < fb.Size.Width; x += stepX)
                pixels.Add(Marshal.ReadInt32(rowAddr, x * 4));
        }
        return pixels;
    }
}
