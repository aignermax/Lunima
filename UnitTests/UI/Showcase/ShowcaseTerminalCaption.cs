using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls;

namespace UnitTests.UI.Showcase;

/// <summary>
/// A terminal-style caption laid over the design canvas for the agent-bridge motif: a dark,
/// translucent panel with monospace lines (<c>&gt; tool_name args</c> followed by
/// <c>  → result</c>). Lives inside the canvas' overlay <see cref="Panel"/> of the real
/// MainWindow, so it is rendered (and captured) like any other UI element.
/// </summary>
internal sealed class ShowcaseTerminalCaption
{
    private const double PanelWidth = 560;
    private const double PanelMargin = 14;
    private const double FontSize = 13.5;
    private static readonly IBrush PanelBrush = new SolidColorBrush(Color.FromArgb(232, 10, 13, 20));
    private static readonly IBrush BorderBrush = new SolidColorBrush(Color.FromArgb(160, 176, 146, 247));
    private static readonly IBrush PromptBrush = new SolidColorBrush(Color.Parse("#B092F7"));
    private static readonly IBrush CallBrush = new SolidColorBrush(Color.Parse("#E6EAF2"));
    private static readonly IBrush ResultBrush = new SolidColorBrush(Color.Parse("#7FD99A"));
    private static readonly IBrush HeaderBrush = new SolidColorBrush(Color.Parse("#8A93A6"));
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");

    private readonly StackPanel _lines = new() { Spacing = 3 };

    /// <summary>Adds the (empty) caption panel over the canvas of <paramref name="window"/>.</summary>
    public ShowcaseTerminalCaption(Window window, string header)
    {
        var canvas = window.GetVisualDescendants().OfType<DesignCanvas>().First();
        var host = (Panel)canvas.GetVisualParent()!;
        _lines.Children.Add(Line(header, HeaderBrush));
        host.Children.Add(new Border
        {
            Width = PanelWidth,
            Margin = new Thickness(PanelMargin),
            Padding = new Thickness(14, 10),
            CornerRadius = new CornerRadius(8),
            Background = PanelBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Child = _lines,
        });
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Appends a dimmed comment line — for what the user did, not a tool call.</summary>
    public void Comment(string text)
    {
        _lines.Children.Add(Line(text, HeaderBrush));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Appends a tool call line (<c>&gt; name args</c>).</summary>
    public void Call(string toolName, string arguments)
    {
        var text = new TextBlock { FontFamily = Mono, FontSize = FontSize, TextWrapping = TextWrapping.Wrap };
        text.Inlines!.Add(new global::Avalonia.Controls.Documents.Run("> ") { Foreground = PromptBrush });
        text.Inlines.Add(new global::Avalonia.Controls.Documents.Run(toolName) { Foreground = CallBrush, FontWeight = FontWeight.SemiBold });
        if (!string.IsNullOrEmpty(arguments))
            text.Inlines.Add(new global::Avalonia.Controls.Documents.Run(" " + arguments) { Foreground = HeaderBrush });
        _lines.Children.Add(text);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Appends the result line of the last call (<c>  → summary</c>).</summary>
    public void Result(string summary)
    {
        _lines.Children.Add(Line("  → " + summary, ResultBrush));
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBlock Line(string text, IBrush brush) => new()
    {
        Text = text,
        FontFamily = Mono,
        FontSize = FontSize,
        Foreground = brush,
        TextWrapping = TextWrapping.Wrap,
    };
}
