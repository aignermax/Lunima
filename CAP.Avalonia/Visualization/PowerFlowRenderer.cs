using Avalonia;
using Avalonia.Media;
using CAP_Core.LightCalculation.PowerFlow;

namespace CAP.Avalonia.Visualization;

/// <summary>
/// Renders power flow visual indicators on waveguide connections.
/// Maps normalized power values to colors and line widths.
/// </summary>
public static class PowerFlowRenderer
{
    /// <summary>
    /// Minimum line width for lowest-power connections.
    /// </summary>
    public const double MinLineWidth = 1.0;

    /// <summary>
    /// Maximum line width for highest-power connections.
    /// </summary>
    public const double MaxLineWidth = 6.0;

    /// <summary>
    /// Opacity for faded-out connections (below threshold).
    /// </summary>
    public const byte FadedOpacity = 40;

    /// <summary>
    /// Creates a pen for rendering a connection based on its power level.
    /// High power = bright green/yellow, thick line.
    /// Low power = dim blue, thin line.
    /// Faded = nearly transparent.
    /// </summary>
    public static Pen CreatePowerPen(ConnectionPowerFlow flow, double fadeThresholdDb)
    {
        bool isFaded = flow.NormalizedPowerDb < fadeThresholdDb;

        if (isFaded)
        {
            return new Pen(
                new SolidColorBrush(Color.FromArgb(FadedOpacity, 80, 80, 120)),
                MinLineWidth);
        }

        double fraction = Math.Clamp(flow.NormalizedPowerFraction, 0, 1);
        var color = InterpolatePowerColor(fraction);
        double width = MinLineWidth + (MaxLineWidth - MinLineWidth) * fraction;

        return new Pen(new SolidColorBrush(color), width);
    }

    /// <summary>
    /// Formats a power value for display on hover.
    /// </summary>
    public static string FormatPowerText(ConnectionPowerFlow flow)
    {
        if (flow.AveragePower <= 0)
            return "No signal";

        string dbText = double.IsNegativeInfinity(flow.NormalizedPowerDb)
            ? "-inf"
            : $"{flow.NormalizedPowerDb:F1}";

        return $"Power: {dbText} dB | {flow.NormalizedPowerFraction * 100:F1}%";
    }

    /// <summary>
    /// Interpolates color based on power fraction.
    /// 0.0 (low) = blue, 0.5 (medium) = orange, 1.0 (high) = bright green.
    /// </summary>
    internal static Color InterpolatePowerColor(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);

        byte r, g, b;

        if (fraction < 0.5)
        {
            // Blue to orange: (80,80,200) -> (255,165,0)
            double t = fraction * 2.0;
            r = (byte)(80 + (255 - 80) * t);
            g = (byte)(80 + (165 - 80) * t);
            b = (byte)(200 + (0 - 200) * t);
        }
        else
        {
            // Orange to bright green: (255,165,0) -> (50,255,50)
            double t = (fraction - 0.5) * 2.0;
            r = (byte)(255 + (50 - 255) * t);
            g = (byte)(165 + (255 - 165) * t);
            b = (byte)(0 + (50 - 0) * t);
        }

        return Color.FromRgb(r, g, b);
    }

    /// <summary>
    /// Draws a power value label at the specified position.
    /// </summary>
    public static void DrawPowerLabel(
        DrawingContext context,
        ConnectionPowerFlow flow,
        Point position)
    {
        var text = FormatPowerText(flow);

        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            10,
            Brushes.White);

        // Draw background for readability
        var textBounds = new Rect(
            position.X - 2,
            position.Y - 2,
            formatted.Width + 4,
            formatted.Height + 4);

        context.FillRectangle(
            new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
            textBounds);

        context.DrawText(formatted, position);
    }
}
