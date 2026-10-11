using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Shouldly;

namespace UnitTests.UI.Redesign;

/// <summary>
/// Renders a window headlessly and writes it to <c>$UI_SHOT_DIR/ui-redesign/dialogs/</c>.
/// No-op without <c>UI_SHOT_DIR</c>, so the screenshot tests cost nothing in normal runs.
/// </summary>
internal static class DialogShot
{
    private const int RenderTicks = 6;

    /// <summary>True when screenshots were requested through the environment.</summary>
    public static bool Enabled => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR"));

    /// <summary>Shows <paramref name="window"/>, captures one frame as <paramref name="name"/>.png and closes it.</summary>
    public static void Capture(Window window, string name, Action? beforeCapture = null)
    {
        var dir = Path.Combine(Environment.GetEnvironmentVariable("UI_SHOT_DIR")!, "ui-redesign", "dialogs");
        Directory.CreateDirectory(dir);
        window.Show();
        try
        {
            Pump();
            beforeCapture?.Invoke();
            Pump();
            using var bitmap = window.CaptureRenderedFrame();
            bitmap.ShouldNotBeNull($"no frame for {name}");
            ScreenshotArtifacts.SavePng(bitmap!, Path.Combine(dir, name + ".png"));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Advances layout and the headless render timer so property changes paint before capture.</summary>
    public static void Pump()
    {
        for (var i = 0; i < RenderTicks; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }
}
