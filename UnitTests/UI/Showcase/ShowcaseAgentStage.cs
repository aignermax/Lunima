using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.Views;

namespace UnitTests.UI.Showcase;

/// <summary>Staging helpers for the agent-bridge and chiplet motifs: panel visibility and
/// framing the canvas on the part of a loaded example the story is about.</summary>
internal static class ShowcaseAgentStage
{
    /// <summary>Hides the properties column (nothing is selected in these motifs).</summary>
    public static void HideRightPanel(Window window)
    {
        window.GetVisualDescendants().OfType<Border>()
            .First(b => b.Name == "RightPanelBorder").IsVisible = false;
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Fits the whole design into the canvas, like the toolbar's zoom-to-fit.</summary>
    public static void ZoomToFit(Window window, MainViewModel vm)
    {
        var canvas = ShowcaseFraming.Canvas(window);
        vm.ZoomToFit(canvas.Bounds.Width, canvas.Bounds.Height);
        canvas.InvalidateVisual();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The design canvas plus the toolbar strip above it (workspace bar and Run
    /// button stay in frame; the left panel is cut off).</summary>
    public static PixelRect CanvasWithToolbar(Window window)
    {
        var canvas = ShowcaseCapture.BoundsIn(window, ShowcaseFraming.Canvas(window));
        return new PixelRect(canvas.X, 0, canvas.Width, canvas.Bottom);
    }

    /// <summary>Zooms the canvas onto the end pins of connection <paramref name="connectionId"/>
    /// with <paramref name="paddingUm"/> of context around them.</summary>
    public static void FocusOnConnection(MainWindow window, MainViewModel vm, string connectionId, double paddingUm)
    {
        var connection = vm.Canvas.Connections
            .First(c => c.Connection.Id.ToString() == connectionId).Connection;
        var (x1, y1) = connection.StartPin.GetAbsolutePosition();
        var (x2, y2) = connection.EndPin.GetAbsolutePosition();
        double minX = Math.Min(x1, x2) - paddingUm, minY = Math.Min(y1, y2) - paddingUm;
        double maxX = Math.Max(x1, x2) + paddingUm, maxY = Math.Max(y1, y2) + paddingUm;
        ShowcaseCircuit.SetView(window, vm, (minX, minY, maxX - minX, maxY - minY));
    }
}
