using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.Views.Panels;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the length-matching panel's meander bend-radius field: the panel
/// after a successful Apply at a 15 µm radius. PNGs land in <c>docs/pr-media/length-matching/</c>
/// with <c>CAP_UPDATE_PR_MEDIA=1</c>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class LengthMatchingRadiusScreenshotTests
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    [AvaloniaFact]
    public void CapturePanelWithBendRadiusAfterApply()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("length-matching");
        Directory.CreateDirectory(dir);
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");

        var canvas = new DesignCanvasViewModel();
        var connection = AddStraightRoute(canvas);
        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        var lengthMatching = vm.BottomPanel.LengthMatching;
        lengthMatching.SelectedConnection = connection;
        lengthMatching.TargetLengthText = "400";
        lengthMatching.BendRadiusText = "15";
        lengthMatching.ApplyCommand.Execute(null);
        lengthMatching.HasSuccessStatus.ShouldBeTrue(lengthMatching.StatusMessage);

        var window = new Window { Width = 420, Height = 420, Content = new LengthMatchingPanel { DataContext = vm } };
        window.Show();
        try
        {
            PumpRenderLoop();
            var bitmap = window.CaptureRenderedFrame();
            bitmap.ShouldNotBeNull();
            SamplePixels(bitmap).ToHashSet().Count.ShouldBeGreaterThan(MinDistinctSampledColors, "near-blank render");
            ScreenshotArtifacts.SavePng(bitmap, Path.Combine(dir, "length-matching-1-bend-radius.png"));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            LocalizationService.Instance.SetLanguage(previousLanguage);
            System.Globalization.CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static WaveguideConnectionViewModel AddStraightRoute(DesignCanvasViewModel canvas)
    {
        var startComp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        startComp.WidthMicrometers = startComp.HeightMicrometers = 250;
        var endComp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        endComp.WidthMicrometers = endComp.HeightMicrometers = 250;
        endComp.PhysicalX = 400;
        canvas.AddComponent(startComp);
        canvas.AddComponent(endComp);

        var startPin = startComp.PhysicalPins.First(p => p.Name == "out");
        var endPin = endComp.PhysicalPins.First(p => p.Name == "in");
        var (sx, sy) = startPin.GetAbsolutePosition();
        var (ex, ey) = endPin.GetAbsolutePosition();
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(sx, sy, ex, ey, 0));
        return canvas.ConnectPinsWithCachedRoute(startPin, endPin, path)!;
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
