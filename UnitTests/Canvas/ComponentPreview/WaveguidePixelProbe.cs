using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CAP.Avalonia.Controls.Rendering;

namespace UnitTests.Canvas.ComponentPreview;

/// <summary>
/// Counts waveguide-core-colored pixels in rendered bitmaps for the canvas-preview
/// visibility journey.
/// </summary>
internal static class WaveguidePixelProbe
{
    /// <summary>
    /// RGB distance under which a pixel counts as waveguide-core colored. The core brush is
    /// <see cref="CanvasPalette.Waveguide"/> at alpha 0xE0; composited over the dark component
    /// body it lands ~30 from the pure color, while the grey of every other layer lands ≥105
    /// away. 70 sits between the two with wide margin on both sides.
    /// </summary>
    public const double ColorDistanceTolerance = 70;

    /// <summary>
    /// Share of waveguide-core-colored pixels inside <paramref name="region"/> of a captured
    /// frame. Reads through <see cref="WriteableBitmap.Lock"/> — the raw framebuffer is the
    /// readback path proven on the headless platform (a captured frame's CopyPixels returns
    /// no content there).
    /// </summary>
    public static double WaveguidePixelShare(WriteableBitmap frame, PixelRect region)
    {
        using var framebuffer = frame.Lock();
        // The headless framebuffer is Rgba8888; honor the declared format instead of
        // assuming BGRA (CopyPixels below delivers BGRA — different contract).
        int redOffset = framebuffer.Format == PixelFormat.Rgba8888 ? 0 : 2;
        int blueOffset = framebuffer.Format == PixelFormat.Rgba8888 ? 2 : 0;
        long core = 0;
        for (int y = region.Y; y < region.Bottom; y++)
        {
            var row = framebuffer.Address + y * framebuffer.RowBytes;
            for (int x = region.X; x < region.Right; x++)
            {
                if (IsWaveguideColored(Marshal.ReadByte(row, x * 4 + redOffset),
                        Marshal.ReadByte(row, x * 4 + 1), Marshal.ReadByte(row, x * 4 + blueOffset)))
                    core++;
            }
        }
        return (double)core / (region.Width * region.Height);
    }

    /// <summary>
    /// Share of waveguide-core-colored pixels inside <paramref name="region"/> of a
    /// rasterised preview bitmap (BGRA, unpainted pixels transparent).
    /// </summary>
    public static double WaveguidePixelShare(RenderTargetBitmap bitmap, PixelRect region)
    {
        int bufferSize = region.Width * region.Height * 4;
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            bitmap.CopyPixels(region, buffer, bufferSize, region.Width * 4);
            long core = 0;
            for (int i = 0; i < region.Width * region.Height; i++)
            {
                if (IsWaveguideColored(Marshal.ReadByte(buffer, i * 4 + 2),
                        Marshal.ReadByte(buffer, i * 4 + 1), Marshal.ReadByte(buffer, i * 4)))
                    core++;
            }
            return (double)core / (region.Width * region.Height);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool IsWaveguideColored(byte red, byte green, byte blue)
    {
        var target = CanvasPalette.Waveguide;
        double dr = red - target.R;
        double dg = green - target.G;
        double db = blue - target.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db) <= ColorDistanceTolerance;
    }
}
