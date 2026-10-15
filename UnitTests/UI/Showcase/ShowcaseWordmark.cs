using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace UnitTests.UI.Showcase;

/// <summary>
/// The Lunima wordmark (waveguide-L + "unima") rebuilt as Avalonia shapes from the committed
/// brand asset <c>docs/media/brand/lunima-wordmark-dark.svg</c>, so the social card always
/// shows the current brand without an SVG renderer. Geometry only — the soft glow of the
/// SVG is left out, as in the app's own wordmark control.
/// </summary>
internal static partial class ShowcaseWordmark
{
    private const double ViewBoxX = 226;
    private const double ViewBoxY = 86;
    private const double ViewBoxWidth = 3061;
    private const double ViewBoxHeight = 788;
    private const double LightStroke = 88;
    private const double CoreStroke = 18;
    private const double EndDotRadius = 70;
    private static readonly Point EndDot = new(800, 740);

    [GeneratedRegex("<path d=\"([^\"]+)\"")]
    private static partial Regex PathData();

    [GeneratedRegex("fill=\"(#[0-9A-Fa-f]{6})\"/></svg>")]
    private static partial Regex LetterFill();

    /// <summary>A wordmark control <paramref name="height"/> px tall (width follows the aspect).</summary>
    public static Control Create(double height)
    {
        var svg = File.ReadAllText(System.IO.Path.Combine(
            ShowcaseCapture.RepoRoot(), "docs", "media", "brand", "lunima-wordmark-dark.svg"));
        var paths = PathData().Matches(svg).Select(m => m.Groups[1].Value).ToList();
        var letters = paths.OrderByDescending(d => d.Length).First();
        var waveguide = paths.First();
        var letterFill = LetterFill().Match(svg) is { Success: true } m ? m.Groups[1].Value : "#E8ECF4";

        var light = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#B092F7"), 0), new GradientStop(Color.Parse("#38C6D4"), 1) },
        };
        var canvas = new global::Avalonia.Controls.Canvas
        {
            Width = ViewBoxWidth, Height = ViewBoxHeight,
            RenderTransform = new TranslateTransform(-ViewBoxX, -ViewBoxY),
            Children =
            {
                Stroke(waveguide, light, LightStroke),
                Stroke(waveguide, new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)), CoreStroke),
                new Path
                {
                    Fill = new SolidColorBrush(Color.Parse("#E8FBFF")),
                    Data = new EllipseGeometry { Center = EndDot, RadiusX = EndDotRadius, RadiusY = EndDotRadius },
                },
                new Path { Fill = new SolidColorBrush(Color.Parse(letterFill)), Data = Geometry.Parse(letters) },
            },
        };
        return new Viewbox
        {
            Height = height,
            Width = height * ViewBoxWidth / ViewBoxHeight,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Child = canvas,
        };
    }

    private static Path Stroke(string data, IBrush brush, double thickness) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = brush,
        StrokeThickness = thickness,
        StrokeLineCap = PenLineCap.Round,
    };
}
