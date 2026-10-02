using Avalonia;
using Avalonia.Controls;
using AnimCanvas = global::Avalonia.Controls.Canvas;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Animated layer of the laser-source help flyout (#1220): the laser dot switches on
/// (its rays fade in), then — once the travelling pulses (the LightPulseAlongPath
/// instances in the flyout) have carried the light into the splitter — the two output
/// branches glow with the power-ramp colors (strong red, weak teal) and stay lit to
/// the end of the loop. The unlit coupler on the far side is static AXAML (a gray
/// "off (output)" dot) — it never glows. Panel-specific, not a reusable primitive —
/// a transparent overlay placed over the flyout's static diagram at the same size; it
/// draws in the diagram's 368×110 coordinate space. The pulses themselves are the
/// LightPulseAlongPath primitive. The loop's last frame keeps the lit end state visible.
/// </summary>
public class LaserInputGlowAnimation : HelpAnimationBase
{
    // Laser-dot geometry in the diagram's coordinate space (dot center 25,55, 18 across).
    private const double LaserDotLeft = 16;
    private const double LaserDotTop = 46;
    private const double LaserDotDiameter = 18;

    // Branch-track geometry: the LightPulseAlongPath instances in the flyout run
    // 240,45 → 336,50 (strong) and 240,65 → 336,60 (weak); the glows sit on top.
    private static readonly Points StrongBranchPoints = new() { new Point(240, 45), new Point(336, 50) };
    private static readonly Points WeakBranchPoints = new() { new Point(240, 65), new Point(336, 60) };

    private const double StrongGlowThickness = 5;
    private const double WeakGlowThickness = 3.5;
    private const double StrongGlowMaxOpacity = 0.9;
    private const double WeakGlowMaxOpacity = 0.5;

    // Cue windows: the laser switches on 0.02…0.12, the input pulse travels 0.15…0.5
    // and the split pulses 0.55…0.9 (both LightPulseAlongPath), the branch glows fade
    // in 0.6…0.85 while the split pulses arrive and stay lit to the end of the loop.
    private static readonly (double Start, double End) LaserOnWindow = (0.02, 0.12);
    private static readonly (double Start, double End) BranchGlowWindow = (0.60, 0.85);

    private static readonly IBrush LaserOnBrush = new ImmutableSolidColorBrush(0xFFFF5252);
    private static readonly IBrush StrongGlowBrush = new ImmutableSolidColorBrush(0xFFFF6450);
    private static readonly IBrush WeakGlowBrush = new ImmutableSolidColorBrush(0xFF00B4B4);

    private readonly Ellipse _laserOnDot;
    private readonly Line[] _rays;
    private readonly Polyline _strongGlow;
    private readonly Polyline _weakGlow;

    /// <summary>Builds the lit laser dot, its rays and the two branch glows at their fixed diagram positions.</summary>
    public LaserInputGlowAnimation()
    {
        _laserOnDot = new Ellipse
        {
            Fill = LaserOnBrush,
            Width = LaserDotDiameter,
            Height = LaserDotDiameter,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        AnimCanvas.SetLeft(_laserOnDot, LaserDotLeft);
        AnimCanvas.SetTop(_laserOnDot, LaserDotTop);

        _rays = CreateRays();

        _strongGlow = new Polyline
        {
            Points = StrongBranchPoints,
            Stroke = StrongGlowBrush,
            StrokeThickness = StrongGlowThickness,
            StrokeLineCap = PenLineCap.Round,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        _weakGlow = new Polyline
        {
            Points = WeakBranchPoints,
            Stroke = WeakGlowBrush,
            StrokeThickness = WeakGlowThickness,
            StrokeLineCap = PenLineCap.Round,
            Opacity = 0,
            IsHitTestVisible = false,
        };

        var canvas = new AnimCanvas { IsHitTestVisible = false, Children = { _strongGlow, _weakGlow, _laserOnDot } };
        foreach (var ray in _rays)
            canvas.Children.Add(ray);
        Content = canvas;
    }

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        var laserOn = Ramp(progress, LaserOnWindow.Start, LaserOnWindow.End);
        _laserOnDot.Opacity = laserOn;
        foreach (var ray in _rays)
            ray.Opacity = laserOn;

        var glow = Ramp(progress, BranchGlowWindow.Start, BranchGlowWindow.End);
        _strongGlow.Opacity = glow * StrongGlowMaxOpacity;
        _weakGlow.Opacity = glow * WeakGlowMaxOpacity;
    }

    private static Line[] CreateRays()
    {
        // Around the dot center (25,55): top, left, right and the two upper diagonals —
        // the same ray layout as the canvas's laser-on icon.
        (double X1, double Y1, double X2, double Y2)[] segments =
        {
            (25, 41, 25, 35),
            (11, 55, 5, 55),
            (39, 55, 45, 55),
            (17, 47, 12, 42),
            (33, 47, 38, 42),
        };
        var rays = new Line[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            rays[i] = new Line
            {
                StartPoint = new Point(segments[i].X1, segments[i].Y1),
                EndPoint = new Point(segments[i].X2, segments[i].Y2),
                Stroke = LaserOnBrush,
                StrokeThickness = 1.5,
                Opacity = 0,
                IsHitTestVisible = false,
            };
        }
        return rays;
    }
}
