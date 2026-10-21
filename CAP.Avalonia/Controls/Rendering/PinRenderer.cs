using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Renders component pins, pin direction indicators, and component name labels.
/// Used internally by <see cref="ComponentRenderer"/>. Dimming in group-edit mode is applied by
/// the caller as an opacity layer, so pins only fade on their own for incompatible drag targets.
/// </summary>
internal sealed class PinRenderer
{
    /// <summary>
    /// World-space font size for a component's name label before the <see cref="PinScreenSize"/>
    /// screen-space cap is applied. Shared with <see cref="LabelDeclutter.ComponentNameLabelComputer"/>
    /// so the overlap-resolution pass measures text at exactly the size actually drawn.
    /// </summary>
    internal const double NameLabelFontSizeWorld = 12.0;

    /// <summary>Screen font size of a hovered pin's name.</summary>
    private const double PinNameFontPx = 11.0;
    private const double FocusedPinMinRadiusPx = 4.0;

    /// <summary>Alpha of a pin that is not a valid target for the connection being dragged.</summary>
    private const byte IncompatibleAlpha = 0x50;

    private static readonly PinBrushes Normal = new(0xFF);
    private static readonly PinBrushes Faint = new(IncompatibleAlpha);
    private static readonly IBrush HighlightGlow = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Accent, 0x55));
    private static readonly IBrush DimmedNameBrush = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Gray300, 0x80));

    /// <summary>
    /// Renders all physical pins of a component.
    /// </summary>
    public void DrawComponentPins(DrawingContext context, ComponentViewModel comp, CanvasRenderContext rc, bool isDimmed = false, bool focusedOnly = false)
    {
        bool isConnectMode = rc.MainViewModel?.CanvasInteraction.CurrentMode == InteractionMode.Connect;
        var highlightedPin = rc.ViewModel.HighlightedPin?.Pin;
        var dragStartPin = rc.InteractionState.ConnectionDragStartPin;
        double pitchScale = focusedOnly ? 1 : PinPitchSizer.ComputeScale(comp.Component.PhysicalPins, isConnectMode ? 8 : 5);
        IEnumerable<PhysicalPin> pins = focusedOnly
            ? highlightedPin is not null && ReferenceEquals(highlightedPin.ParentComponent, comp.Component)
                ? new[] { highlightedPin } : Array.Empty<PhysicalPin>()
            : comp.Component.PhysicalPins;

        foreach (var pin in pins)
        {
            var (pinX, pinY) = pin.GetAbsolutePosition();
            bool isHighlighted = pin == highlightedPin;

            // During a connection drag, dim incompatible pins (wrong signal domain or wrong
            // polarization) so the user sees which targets are valid before releasing
            // (issue #534, extended to the domain check in #724 point 4).
            bool isIncompatibleTarget = dragStartPin != null && pin != dragStartPin &&
                PinConnectionAffordance.IsIncompatibleTarget(dragStartPin, pin);
            var brushes = isIncompatibleTarget ? Faint : Normal;

            double pinSize = PinScreenSize.CapWorldRadius((isConnectMode ? 8 : 5) * pitchScale, rc.Zoom);
            if (isHighlighted)
            {
                pinSize = PinScreenSize.CapWorldRadius(12 * pitchScale, rc.Zoom);
                if (focusedOnly)
                    pinSize = Math.Max(pinSize, FocusedPinMinRadiusPx / (rc.Zoom <= 0 ? 1 : rc.Zoom));
                context.DrawEllipse(HighlightGlow, null, new Point(pinX, pinY), pinSize * 1.5, pinSize * 1.5);
            }

            DrawPinDirectionIndicator(context, pin, pinX, pinY, isHighlighted, rc, pitchScale);
            DrawPinShape(context, pin, brushes.For(pin, isHighlighted, isConnectMode), brushes.Rim, pinX, pinY, pinSize);

            if (isHighlighted)
            {
                // Deferred to the topmost label pass: the hover/connect pin name must stay
                // readable even where a neighboring component's body would paint over it.
                var pinText = new FormattedText(
                    pin.Name,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    CanvasTypography.UiMedium,
                    PinNameFontPx / (rc.Zoom <= 0 ? 1 : rc.Zoom),
                    CanvasPalette.AccentHoverBrush);
                rc.Labels.Enqueue(pinText, CanvasPalette.AccentHoverBrush, new Point(pinX + 15, pinY - 15));
            }
        }
    }

    /// <summary>
    /// Enqueues the component name label (anchored below the footprint for flat components,
    /// inside the top-left corner otherwise — see
    /// <see cref="LabelDeclutter.ComponentNameLabelComputer.GetLabelAnchor"/>) into the
    /// deferred topmost pass, using the <see cref="FormattedText"/>
    /// <see cref="LabelDeclutter.ComponentNameLabelComputer"/> already measured. Names at rest
    /// use the secondary text color (or <paramref name="highlightBrush"/>, e.g. a light endpoint
    /// in Optics); a selected or hovered name is promoted to primary.
    /// </summary>
    public void DrawComponentName(DeferredLabelLayer labels, ComponentViewModel comp, FormattedText labelText,
        bool isDimmed = false, bool isEmphasized = false, IBrush? highlightBrush = null)
    {
        IBrush brush = isDimmed ? DimmedNameBrush
            : isEmphasized ? CanvasPalette.TextPrimaryBrush
            : highlightBrush ?? CanvasPalette.TextSecondaryBrush;
        labels.Enqueue(labelText, brush,
            LabelDeclutter.ComponentNameLabelComputer.GetLabelAnchor(comp, labelText.Height));
    }

    /// <summary>
    /// Draws the pin marker glyph resolved by <see cref="PinGlyphSelector"/> — the single
    /// place deciding shape from (MatterType × Polarization), so an electrical pin can never
    /// pick up a polarization shape and a TM pin can never look electrical (issue #724).
    /// Color already encodes <see cref="MatterType"/> via <see cref="PinBrushes.For"/>.
    /// </summary>
    private static void DrawPinShape(DrawingContext context, PhysicalPin pin, IBrush brush, IBrush rim,
        double pinX, double pinY, double pinSize)
    {
        switch (PinGlyphSelector.SelectGlyph(pin.MatterType, pin.Polarization))
        {
            case PinGlyph.ElectricalPad:
                var rect = new Rect(pinX - pinSize, pinY - pinSize, pinSize * 2, pinSize * 2);
                context.DrawRectangle(brush, new Pen(rim, pinSize * 0.3), rect);
                break;
            case PinGlyph.OpticalDiamond:
                context.DrawGeometry(brush, null, BuildDiamondGeometry(pinX, pinY, pinSize));
                break;
            case PinGlyph.OpticalCircleWithDiamondOutline:
                context.DrawEllipse(brush, null, new Point(pinX, pinY), pinSize, pinSize);
                context.DrawGeometry(null, new Pen(rim, pinSize * 0.2), BuildDiamondGeometry(pinX, pinY, pinSize));
                break;
            default:
                context.DrawEllipse(brush, null, new Point(pinX, pinY), pinSize, pinSize);
                break;
        }
    }

    /// <summary>Builds a diamond (rotated square) geometry centered at the pin position.</summary>
    private static StreamGeometry BuildDiamondGeometry(double pinX, double pinY, double pinSize)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(pinX, pinY - pinSize), true);
            ctx.LineTo(new Point(pinX + pinSize, pinY));
            ctx.LineTo(new Point(pinX, pinY + pinSize));
            ctx.LineTo(new Point(pinX - pinSize, pinY));
            ctx.EndFigure(true);
        }
        return geometry;
    }

    private static void DrawPinDirectionIndicator(DrawingContext context, PhysicalPin pin, double pinX, double pinY,
        bool isHighlighted, CanvasRenderContext rc, double pitchScale)
    {
        var pen = isHighlighted ? rc.Pens.Selection : rc.Pens.PinDirection;
        double angle = pin.GetAbsoluteAngle() * Math.PI / 180;
        double dirLength = PinScreenSize.CapWorldRadius((isHighlighted ? 20 : 15) * pitchScale, rc.Zoom);
        context.DrawLine(pen,
            new Point(pinX, pinY),
            new Point(pinX + Math.Cos(angle) * dirLength, pinY + Math.Sin(angle) * dirLength));
    }

    /// <summary>Pin fills at one alpha level, allocated once.</summary>
    private sealed class PinBrushes
    {
        private readonly IBrush _optical;
        private readonly IBrush _unlinked;
        private readonly IBrush _electrical;
        private readonly IBrush _connectTarget;
        private readonly IBrush _highlighted;

        public PinBrushes(byte alpha)
        {
            _optical = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.PinOptical, alpha));
            _unlinked = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Danger, alpha));
            _electrical = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Lighten(CanvasPalette.Copper, 0.15), alpha));
            _connectTarget = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.AccentHover, alpha));
            _highlighted = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Gray100, alpha));
            Rim = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Gray950, alpha));
        }

        /// <summary>Dark contact rim / outline color.</summary>
        public IBrush Rim { get; }

        /// <summary>Fill for a pin: highlight wins, then the electrical domain, connect mode, logical link.</summary>
        public IBrush For(PhysicalPin pin, bool isHighlighted, bool isConnectMode)
        {
            if (isHighlighted) return _highlighted;
            if (pin.MatterType == MatterType.Electricity) return _electrical;
            if (isConnectMode) return _connectTarget;
            return pin.LogicalPin != null ? _optical : _unlinked;
        }
    }
}
