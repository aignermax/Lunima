using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Renders screen-space overlays: the interaction mode chip (top right) and the status line
/// (bottom left), both as quiet translucent chips in the app's type and colors.
/// Implements <see cref="ICanvasRenderer"/> and draws in screen space (outside world transform).
/// </summary>
public sealed class CanvasOverlayRenderer : ICanvasRenderer
{
    private const double Margin = 12.0;
    private const double ChipPadX = 10.0;
    private const double ChipHeight = 24.0;
    private const double ChipRadius = 6.0;
    private const double DotRadius = 3.5;
    private const double DotGap = 8.0;
    private const double ModeFontSize = 12.0;
    private const double StatusFontSize = 11.0;

    private static readonly IBrush ChipBrush = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Gray800, 0xD8));
    private static readonly IPen ChipBorderPen = new Pen(CanvasPalette.Brush(CanvasPalette.Gray700), 1);
    private static readonly IBrush InfoBrush = CanvasPalette.Brush(CanvasPalette.Info);

    /// <inheritdoc/>
    public void Render(DrawingContext context, CanvasRenderContext rc)
    {
        DrawModeIndicator(context, rc.Bounds, rc.MainViewModel);
        DrawStatusInfo(context, rc.Bounds, rc.ViewModel, rc.Zoom);
    }

    private static void DrawModeIndicator(DrawingContext context, Rect bounds, MainViewModel? mainVm)
    {
        if (mainVm == null) return;

        var loc = LocalizationService.Instance;
        var mode = mainVm.CanvasInteraction.CurrentMode;
        string modeText = mode switch
        {
            InteractionMode.Select => $"{loc.Translate("Canvas.Mode.Select")}  S",
            InteractionMode.PlaceComponent => $"{loc.Translate("Canvas.Mode.Place")}  P",
            InteractionMode.Connect => $"{loc.Translate("Canvas.Mode.Connect")}  C",
            InteractionMode.Delete => $"{loc.Translate("Canvas.Mode.Delete")}  D",
            InteractionMode.Cut => $"{loc.Translate("Canvas.Mode.Cut")}  X",
            _ => ""
        };
        if (modeText.Length == 0) return;

        var text = new FormattedText(modeText, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, CanvasTypography.UiMedium, ModeFontSize, CanvasPalette.TextPrimaryBrush);
        double width = ChipPadX * 2 + DotRadius * 2 + DotGap + text.Width;
        var chip = new Rect(bounds.Width - Margin - width, Margin, width, ChipHeight);
        context.DrawRectangle(ChipBrush, ChipBorderPen, chip, ChipRadius, ChipRadius);
        context.DrawEllipse(ModeBrush(mode), null,
            new Point(chip.X + ChipPadX + DotRadius, chip.Y + ChipHeight / 2), DotRadius, DotRadius);
        context.DrawText(text, new Point(chip.X + ChipPadX + DotRadius * 2 + DotGap, chip.Y + (ChipHeight - text.Height) / 2));
    }

    /// <summary>Mode dot color: accent for select, then the semantic color the mode stands for.</summary>
    private static IBrush ModeBrush(InteractionMode mode) => mode switch
    {
        InteractionMode.PlaceComponent => CanvasPalette.SuccessBrush,
        InteractionMode.Connect => InfoBrush,
        InteractionMode.Delete => CanvasPalette.DangerBrush,
        InteractionMode.Cut => CanvasPalette.WarningBrush,
        _ => CanvasPalette.AccentBrush,
    };

    private static void DrawStatusInfo(DrawingContext context, Rect bounds, DesignCanvasViewModel vm, double zoom)
    {
        // The active fabrication process is shown here (grid HUD) rather than only at the
        // bottom of the PDK panel; the snap/power key hints were dropped from the HUD because
        // the full key legend is already displayed under the canvas.
        string processInfo = string.IsNullOrEmpty(vm.ActiveProcessLabel) ? "" : $"   ·   {vm.ActiveProcessLabel}";

        var loc = LocalizationService.Instance;
        string zoomLabel = loc.Translate("Canvas.Hud.Zoom");
        string componentsLabel = loc.Translate("Canvas.Hud.Components");
        string connectionsLabel = loc.Translate("Canvas.Hud.Connections");

        var text = new FormattedText(
            $"{zoomLabel} {zoom:P0}   ·   {componentsLabel} {vm.Components.Count}   ·   {connectionsLabel} {vm.Connections.Count}{processInfo}",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            CanvasTypography.Ui,
            StatusFontSize,
            CanvasPalette.TextSecondaryBrush);
        var chip = new Rect(Margin, bounds.Height - Margin - ChipHeight, text.Width + ChipPadX * 2, ChipHeight);
        context.DrawRectangle(ChipBrush, ChipBorderPen, chip, ChipRadius, ChipRadius);
        context.DrawText(text, new Point(chip.X + ChipPadX, chip.Y + (ChipHeight - text.Height) / 2));
    }
}
