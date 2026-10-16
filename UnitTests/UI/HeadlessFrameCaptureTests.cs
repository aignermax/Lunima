using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace UnitTests.UI;

public class HeadlessFrameCaptureTests
{
    [AvaloniaFact]
    public void MissingCompositorFrame_RendersActualScenePixelsAtWindowSize()
    {
        var window = new Window
        {
            Width = 120, Height = 80,
            Content = new Border { Background = Brushes.Lime }
        };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            using var frame = HeadlessFrameCapture.Capture(window, _ => null);
            Assert.Equal(new PixelSize(120, 80), frame.PixelSize);
            using var pixels = frame.Lock();
            var center = pixels.Address + 40 * pixels.RowBytes + 60 * 4;
            Assert.Equal(0, Marshal.ReadByte(center));
            Assert.Equal(255, Marshal.ReadByte(center, 1));
            Assert.Equal(0, Marshal.ReadByte(center, 2));
            Assert.Equal(255, Marshal.ReadByte(center, 3));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AvailableCompositorFrame_IsReturnedWithoutReplacingIt()
    {
        using var supplied = new WriteableBitmap(new PixelSize(12, 8), new Vector(96, 96));
        var window = new Window();
        Assert.Same(supplied, HeadlessFrameCapture.Capture(window, _ => supplied));
    }
}
