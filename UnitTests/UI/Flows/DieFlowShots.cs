using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.Views;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using Shouldly;
using UnitTests.UI.Showcase;

namespace UnitTests.UI.Flows;

/// <summary>
/// Canvas coordinates and PR screenshots for the die work-surface flows: world → window
/// points, a view fitted around dies, and captures written to
/// <c>docs/pr-media/die-work-surface/</c> when <c>UI_SHOT_DIR</c> is set.
/// </summary>
internal static class DieFlowShots
{
    private const int CaptureAttempts = 3;
    private const double FitMarginMicrometers = 250;

    /// <summary>The screenshot directory, or null when screenshots are not requested.</summary>
    public static string? Directory()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return null;
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("die-work-surface");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Fits the view around the given dies.</summary>
    public static void Fit(MainWindow win, MainViewModel vm, params ComponentGroup[] dies)
    {
        var all = DieAssembly.CarrierOutline(dies)!.Value;
        ShowcaseCircuit.SetView(win, vm, (all.X - FitMarginMicrometers, all.Y - FitMarginMicrometers,
            all.Width + 2 * FitMarginMicrometers, all.Height + 2 * FitMarginMicrometers));
    }

    /// <summary>Window point of canvas world coordinates (µm).</summary>
    public static Point CanvasPoint(MainWindow win, MainViewModel vm, double x, double y)
    {
        var canvas = UiInput.Descendants<DesignCanvas>(win).First();
        return canvas.TranslatePoint(new Point(x * canvas.Zoom + vm.Canvas.PanX, y * canvas.Zoom + vm.Canvas.PanY), win)!.Value;
    }

    /// <summary>Writes the rendered window to <paramref name="name"/> (no-op without a directory).</summary>
    public static void Capture(MainWindow window, string? directory, string name)
    {
        if (directory == null)
            return;
        ScreenshotArtifacts.SettleAnimations();
        WriteableBitmap? bitmap = null;
        for (int attempt = 0; attempt < CaptureAttempts; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var frame = window.CaptureRenderedFrame();
            if (frame == null)
                continue;
            bitmap?.Dispose();
            bitmap = frame;
        }
        bitmap.ShouldNotBeNull($"no frame captured for {name}");
        using (bitmap)
            ScreenshotArtifacts.SavePng(bitmap, Path.Combine(directory, name));
    }
}
