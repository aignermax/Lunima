using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Renders screen-space overlays: interaction mode indicator and status bar.
/// Implements <see cref="ICanvasRenderer"/> and draws in screen space (outside world transform).
/// </summary>
public sealed class CanvasOverlayRenderer : ICanvasRenderer
{
    /// <inheritdoc/>
    public void Render(DrawingContext context, CanvasRenderContext rc)
    {
        DrawModeIndicator(context, rc.Bounds, rc.MainViewModel);
        DrawStatusInfo(context, rc.Bounds, rc.ViewModel, rc.Zoom);
    }

    private static void DrawModeIndicator(DrawingContext context, Rect bounds, MainViewModel? mainVm)
    {
        if (mainVm == null) return;

        string modeText = mainVm.CanvasInteraction.CurrentMode switch
        {
            InteractionMode.Select => "[S] Select",
            InteractionMode.PlaceComponent => "[P] Place",
            InteractionMode.Connect => "[C] Connect",
            InteractionMode.Delete => "[D] Delete",
            _ => ""
        };

        IBrush brush = mainVm.CanvasInteraction.CurrentMode switch
        {
            InteractionMode.Select => Brushes.LightBlue,
            InteractionMode.PlaceComponent => Brushes.LightGreen,
            InteractionMode.Connect => Brushes.Orange,
            InteractionMode.Delete => Brushes.Red,
            _ => Brushes.White
        };

        context.DrawText(
            new FormattedText(modeText, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial", FontStyle.Normal, FontWeight.Bold),
                14, brush),
            new Point(bounds.Width - 100, 10));
    }

    private static void DrawStatusInfo(DrawingContext context, Rect bounds, DesignCanvasViewModel vm, double zoom)
    {
        string snapInfo = vm.GridSnap.IsEnabled
            ? $" | [G] Snap: {vm.GridSnap.GridSizeMicrometers}µm"
            : " | [G] Snap: OFF";
        string gridInfo = vm.ShowGridOverlay ? " | [Shift+G] Grid: ON" : "";
        string powerInfo = vm.ShowPowerFlow ? " | [P] Power: ON" : " | [P] Power: OFF";

        context.DrawText(
            new FormattedText(
                $"Zoom: {zoom:P0} | Components: {vm.Components.Count} | Connections: {vm.Connections.Count}{snapInfo}{gridInfo}{powerInfo}",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                12,
                Brushes.White),
            new Point(10, bounds.Height - 25));
    }
}
