using Avalonia;
using Avalonia.Controls;
using AnimCanvas = global::Avalonia.Controls.Canvas;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Animated layer of the process-management help flyout (#1152): the chip is built
/// by stacking patterned material layers one after another (substrate → core →
/// cladding → metal); each layer fades and rises into place in sequence.
/// Panel-specific, not a reusable primitive. Transparent overlay in the flyout
/// diagram's coordinate space (360×112); the static labels stay in AXAML beneath it.
/// The loop's last frame keeps the finished stack visible.
/// </summary>
public class LayerStackAnimation : HelpAnimationBase
{
    private sealed record LayerSpec(
        double Left, double TopFrom, double TopTo, double Width, double Height,
        uint Color, double MaxOpacity, double CueStart, double CueEnd);

    // Same cue windows as the pre-#1152 XAML keyframe animation, minus the fade-out
    // (the scrubbed end state now keeps the finished stack visible).
    private static readonly LayerSpec[] Layers =
    {
        new(Left: 30, TopFrom: 96, TopTo: 88, Width: 270, Height: 14, Color: 0xFF666666, MaxOpacity: 1.00, CueStart: 0.00, CueEnd: 0.12),
        new(Left: 30, TopFrom: 82, TopTo: 74, Width: 270, Height: 12, Color: 0xFF4CAF50, MaxOpacity: 1.00, CueStart: 0.22, CueEnd: 0.34),
        new(Left: 30, TopFrom: 68, TopTo: 60, Width: 270, Height: 14, Color: 0xFF3F51B5, MaxOpacity: 0.55, CueStart: 0.44, CueEnd: 0.56),
        new(Left: 110, TopFrom: 54, TopTo: 46, Width: 110, Height: 12, Color: 0xFFDAA520, MaxOpacity: 1.00, CueStart: 0.66, CueEnd: 0.78),
    };

    private readonly Rectangle[] _rectangles;

    /// <summary>Builds one rectangle per material layer.</summary>
    public LayerStackAnimation()
    {
        _rectangles = new Rectangle[Layers.Length];
        var canvas = new AnimCanvas { IsHitTestVisible = false };
        for (int i = 0; i < Layers.Length; i++)
        {
            var spec = Layers[i];
            _rectangles[i] = new Rectangle
            {
                Width = spec.Width,
                Height = spec.Height,
                Fill = new SolidColorBrush(spec.Color),
                Opacity = 0,
                IsHitTestVisible = false,
            };
            AnimCanvas.SetLeft(_rectangles[i], spec.Left);
            AnimCanvas.SetTop(_rectangles[i], spec.TopFrom);
            canvas.Children.Add(_rectangles[i]);
        }
        Content = canvas;
    }

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        for (int i = 0; i < Layers.Length; i++)
        {
            var spec = Layers[i];
            double ramp = Ramp(progress, spec.CueStart, spec.CueEnd);
            _rectangles[i].Opacity = ramp * spec.MaxOpacity;
            AnimCanvas.SetTop(_rectangles[i], spec.TopFrom + (spec.TopTo - spec.TopFrom) * ramp);
        }
    }
}
