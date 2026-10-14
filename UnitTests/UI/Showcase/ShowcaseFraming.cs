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

    /// <summary>The first visual descendant of type <typeparamref name="T"/> named <paramref name="name"/>.</summary>
    public static T Named<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    /// <summary>The window-space bounds of a named control.</summary>
    public static PixelRect Named(Window window, string name) =>
        ShowcaseCapture.BoundsIn(window, window.GetVisualDescendants().OfType<Control>()
            .First(c => c.Name == name));

    /// <summary>Hides the properties column — nothing is selected in most motifs, and the
    /// canvas reads better with the space.</summary>
    public static void HideRightPanel(Window window)
    {
        window.GetVisualDescendants().OfType<Border>()
            .First(b => b.Name == "RightPanelBorder").IsVisible = false;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Sets the properties column to a fixed width (it is content-driven otherwise).</summary>
    public static void SetRightPanelWidth(Window window, double width)
    {
        window.GetVisualDescendants().OfType<Border>()
            .First(b => b.Name == "RightPanelBorder").Width = width;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Scrolls the properties column so <paramref name="target"/>'s bottom edge is in view
    /// (the Make-gate results sit below the pin table).</summary>
    public static void ScrollRightPanelTo(Window window, Visual target)
    {
        var panel = window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "RightPanelBorder");
        var scroller = panel.GetVisualDescendants().OfType<ScrollViewer>()
            .OrderByDescending(s => s.Extent.Height).First();
        var top = target.TranslatePoint(new Point(0, 0), scroller.Content as Visual ?? scroller);
        if (top is { } p)
            scroller.Offset = new Vector(0, Math.Max(0, p.Y + target.Bounds.Height - scroller.Viewport.Height));
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Centers the canvas on the components' bounding box at the largest zoom that
    /// keeps a <paramref name="marginPx"/> border (zoom-to-fit also fits the chip outline).</summary>
    public static void FitComponents(Window window, MainViewModel vm, double marginPx = 60)
    {
        var box = CAP.Avalonia.ViewModels.Canvas.BoundingBoxCalculator.Calculate(vm.Canvas.Components);
        if (box is not { } b) return;
        var canvas = Canvas(window);
        double zoom = Math.Min((canvas.Bounds.Width - 2 * marginPx) / b.Width,
            (canvas.Bounds.Height - 2 * marginPx) / b.Height);
        canvas.Zoom = zoom;
        vm.Canvas.PanX = canvas.Bounds.Width / 2 - b.CenterX * zoom;
        vm.Canvas.PanY = canvas.Bounds.Height / 2 - b.CenterY * zoom;
        canvas.InvalidateVisual();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Fits the whole design into the canvas, like the toolbar's zoom-to-fit.</summary>
    public static void ZoomToFit(Window window, MainViewModel vm)
    {
        var canvas = Canvas(window);
        vm.ZoomToFit(canvas.Bounds.Width, canvas.Bounds.Height);
        canvas.InvalidateVisual();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The window's design canvas control.</summary>
    public static DesignCanvas Canvas(Window window) =>
        window.GetVisualDescendants().OfType<DesignCanvas>().First();
}
