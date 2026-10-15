using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace UnitTests.UI;

/// <summary>
/// Captures a shown headless window as a bitmap for scene screenshot tests.
/// <para>
/// The compositor's frame (<see cref="HeadlessWindowExtensions.CaptureRenderedFrame"/>) is
/// preferred. Late in the long single-process Linux CI shard the headless compositor can
/// stop delivering frames for every window — even a fresh 300×200 probe, after a full GC,
/// with no render exception logged — while the same visual still renders synchronously into
/// a <see cref="RenderTargetBitmap"/>. The scene under test is then drawn that way instead,
/// so a harness stall no longer fails a test whose subject is the scene's drawing.
/// </para>
/// </summary>
internal static class HeadlessFrameCapture
{
    /// <summary>How long the compositor gets to deliver a frame before the synchronous fallback.</summary>
    public const int CompositorTimeoutMilliseconds = 2000;

    private const int RetryDelayMilliseconds = 50;
    private const double DefaultDpi = 96;

    /// <summary>
    /// Captures <paramref name="window"/>: its last compositor frame when one arrives within
    /// <see cref="CompositorTimeoutMilliseconds"/>, otherwise a synchronous render of the window.
    /// The caller owns (disposes) the returned bitmap.
    /// </summary>
    public static WriteableBitmap Capture(Window window)
    {
        return TryCompositorFrame(window) ?? RenderSynchronously(window);
    }

    private static WriteableBitmap? TryCompositorFrame(Window window)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var frame = window.CaptureRenderedFrame();
            if (frame != null || elapsed.ElapsedMilliseconds > CompositorTimeoutMilliseconds)
                return frame;
            Thread.Sleep(RetryDelayMilliseconds);
        }
    }

    private static WriteableBitmap RenderSynchronously(Window window)
    {
        var size = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Width)),
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Height)));
        var dpi = new Vector(DefaultDpi, DefaultDpi);
        using var rendered = new RenderTargetBitmap(size, dpi);
        rendered.Render(window);

        var bitmap = new WriteableBitmap(size, dpi, Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            rendered.CopyPixels(new PixelRect(size), buffer.Address,
                buffer.RowBytes * size.Height, buffer.RowBytes);
        }
        return bitmap;
    }
}
