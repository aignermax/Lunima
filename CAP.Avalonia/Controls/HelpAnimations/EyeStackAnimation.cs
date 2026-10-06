using Avalonia;
using Avalonia.Controls;
using AnimCanvas = global::Avalonia.Controls.Canvas;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Animated layer of the Eye/BER help flyout (#1152): four eye trajectories fade in
/// one after another (bit slices stacking), then the dashed "eye opening" marker
/// appears. Panel-specific, not a reusable primitive. It is a transparent overlay:
/// place it over the flyout's static diagram (waveform + arrow) at the same size —
/// the eye traces are drawn in the full diagram's coordinate space (380×90).
/// The loop's last frame keeps the complete eye visible.
/// </summary>
public class EyeStackAnimation : HelpAnimationBase
{
    private const double TraceMaxOpacity = 0.9;

    // Same cue windows as the pre-#1152 XAML keyframe animation, minus the fade-out
    // (the scrubbed end state now keeps the finished eye visible).
    private static readonly (double Start, double End)[] TraceWindows =
        { (0.00, 0.10), (0.18, 0.28), (0.36, 0.46), (0.54, 0.64) };

    private static readonly (double Start, double End) OpeningWindow = (0.70, 0.80);

    private static readonly string[] TraceData =
    {
        "M 215,25 L 355,25",
        "M 215,65 L 355,65",
        "M 215,25 C 265,25 305,65 355,65",
        "M 215,65 C 265,65 305,25 355,25",
    };

    private readonly Path[] _traces;
    private readonly Ellipse _opening;

    /// <summary>Builds the four trace paths and the eye-opening marker.</summary>
    public EyeStackAnimation()
    {
        _traces = new Path[TraceData.Length];
        var canvas = new AnimCanvas { IsHitTestVisible = false };
        for (int i = 0; i < _traces.Length; i++)
        {
            _traces[i] = new Path
            {
                Data = StreamGeometry.Parse(TraceData[i]),
                Stroke = new SolidColorBrush(0xFF4FC3F7),
                StrokeThickness = 2,
                Opacity = 0,
                IsHitTestVisible = false,
            };
            canvas.Children.Add(_traces[i]);
        }

        _opening = new Ellipse
        {
            Width = 40,
            Height = 24,
            Stroke = new SolidColorBrush(0xFFFFD54F),
            StrokeThickness = 2,
            StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double> { 3, 2 },
            Opacity = 0,
            IsHitTestVisible = false,
        };
        AnimCanvas.SetLeft(_opening, 265);
        AnimCanvas.SetTop(_opening, 33);
        canvas.Children.Add(_opening);

        Content = canvas;
    }

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        for (int i = 0; i < _traces.Length; i++)
            _traces[i].Opacity = Ramp(progress, TraceWindows[i].Start, TraceWindows[i].End) * TraceMaxOpacity;
        _opening.Opacity = Ramp(progress, OpeningWindow.Start, OpeningWindow.End);
    }
}
