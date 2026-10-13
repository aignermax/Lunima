using Avalonia;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Session;

/// <summary>
/// Renders a µm region of the design to PNG with the real canvas renderer: an off-screen
/// <see cref="DesignCanvas"/> bound to the live canvas view-model. The view-model carries
/// the pan offset and the repaint callback of the on-screen canvas, so both are saved and
/// restored around the render — all inside one UI-thread job, so the user's view never
/// paints the borrowed state. Must run on the UI thread.
/// </summary>
public sealed class CanvasSnapshotRenderer
{
    /// <summary>Largest edge of a rendered snapshot in pixels.</summary>
    public const int MaxEdgePixels = 4096;

    private const double Dpi = 96;

    private readonly Func<MainViewModel?> _mainViewModel;

    /// <summary>Creates the renderer.</summary>
    /// <param name="mainViewModel">
    /// Resolves the main view-model (GDS previews, layer visibility); null renders the
    /// plain schematic view.
    /// </param>
    public CanvasSnapshotRenderer(Func<MainViewModel?> mainViewModel) => _mainViewModel = mainViewModel;

    /// <summary>Renders <paramref name="region"/> (µm) at <paramref name="widthPixels"/> wide.</summary>
    /// <returns>PNG bytes and the pixel size actually rendered.</returns>
    public (byte[] Png, int Width, int Height) Render(
        DesignCanvasViewModel canvas, Rect region, int widthPixels)
    {
        var zoom = FitZoom(region, widthPixels);
        var width = Math.Clamp((int)Math.Round(region.Width * zoom), 1, MaxEdgePixels);
        var height = Math.Clamp((int)Math.Round(region.Height * zoom), 1, MaxEdgePixels);

        var saved = (canvas.PanX, canvas.PanY, canvas.ViewZoom, canvas.RepaintRequested);
        var control = new DesignCanvas { ViewModel = canvas, MainViewModel = _mainViewModel(), Zoom = zoom };
        try
        {
            canvas.PanX = -region.X * zoom;
            canvas.PanY = -region.Y * zoom;
            var size = new Size(width, height);
            control.Measure(size);
            control.Arrange(new Rect(size));

            using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(Dpi, Dpi));
            bitmap.Render(control);
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            return (stream.ToArray(), width, height);
        }
        finally
        {
            control.MainViewModel = null;
            control.ViewModel = null;
            canvas.PanX = saved.PanX;
            canvas.PanY = saved.PanY;
            canvas.ViewZoom = saved.ViewZoom;
            canvas.RepaintRequested = saved.RepaintRequested;
        }
    }

    /// <summary>Zoom (px per µm) that fits the region into the requested width and the edge limit.</summary>
    public static double FitZoom(Rect region, int widthPixels)
    {
        var zoom = widthPixels / region.Width;
        var maxZoomForHeight = MaxEdgePixels / region.Height;
        return Math.Min(zoom, maxZoomForHeight);
    }
}
