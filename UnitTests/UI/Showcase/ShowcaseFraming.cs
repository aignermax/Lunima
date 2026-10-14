using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels;

namespace UnitTests.UI.Showcase;

/// <summary>
/// Window-space crop rectangles for the showcase GIFs — computed once per recording so
/// every frame shares one frame of reference and the loop holds perfectly still.
/// </summary>
internal static class ShowcaseFraming
{
    /// <summary>The toolbar (with the workspace bar), left panel and canvas — everything
    /// above the bottom of the design canvas (dock strips and status bar cut off).</summary>
    public static PixelRect WindowDownToCanvasBottom(Window window)
    {
        var canvas = ShowcaseCapture.BoundsIn(window, Canvas(window));
        return new PixelRect(0, 0, (int)window.Bounds.Width, canvas.Bottom);
    }

    /// <summary>The window above the status bar (toolbar, panels, canvas and dock).</summary>
    public static PixelRect WindowWithoutStatusBar(Window window)
    {
        var statusBar = window.GetVisualDescendants().OfType<Control>()
            .First(c => c.Name == "StatusBarRoot");
        var top = ShowcaseCapture.BoundsIn(window, statusBar).Y;
        return new PixelRect(0, 0, (int)window.Bounds.Width, top);
    }

    /// <summary>The window-space rectangle of a world-space (µm) canvas region at the
    /// current zoom/pan, clipped to the canvas.</summary>
    public static PixelRect CanvasWorld(
        Window window, MainViewModel vm, (double X, double Y, double W, double H) region)
    {
        var canvasControl = Canvas(window);
        double zoom = canvasControl.Zoom;
        var bounds = ShowcaseCapture.BoundsIn(window, canvasControl);
        return new PixelRect(
                bounds.X + (int)(region.X * zoom + vm.Canvas.PanX),
                bounds.Y + (int)(region.Y * zoom + vm.Canvas.PanY),
                (int)(region.W * zoom), (int)(region.H * zoom))
            .Intersect(bounds);
    }

    /// <summary>The window-space bounds of a named control.</summary>
    public static PixelRect Named(Window window, string name) =>
        ShowcaseCapture.BoundsIn(window, window.GetVisualDescendants().OfType<Control>()
            .First(c => c.Name == name));

    /// <summary>The window's design canvas control.</summary>
    public static DesignCanvas Canvas(Window window) =>
        window.GetVisualDescendants().OfType<DesignCanvas>().First();
}
