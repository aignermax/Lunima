using Avalonia;
using Avalonia.Data;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;

namespace CAP.Avalonia.Controls;

/// <summary>
/// Assembly editor of the ISA playground: an editable program with line numbers and
/// the line the program counter points at highlighted in place, so the program is
/// shown once — not as an editor plus a separate trace listing.
/// </summary>
public class IsaProgramEditor : TextEditor
{
    /// <summary>The program text, bindable two-way (AvaloniaEdit's own Text is a plain CLR property).</summary>
    public static readonly StyledProperty<string?> CodeProperty =
        AvaloniaProperty.Register<IsaProgramEditor, string?>(nameof(Code), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>The 1-based line to highlight as the current step, or null for none.</summary>
    public static readonly StyledProperty<int?> CurrentLineProperty =
        AvaloniaProperty.Register<IsaProgramEditor, int?>(nameof(CurrentLine));

    private readonly CurrentLineHighlighter _highlighter = new();
    private bool _syncingFromProperty;

    /// <summary>Creates the editor with line numbers and the current-step highlighter installed.</summary>
    public IsaProgramEditor()
    {
        ShowLineNumbers = true;
        TextArea.TextView.BackgroundRenderers.Add(_highlighter);
        TextChanged += (_, _) =>
        {
            if (!_syncingFromProperty)
                SetCurrentValue(CodeProperty, Text);
        };
    }

    /// <summary>Inherits the stock TextEditor template and theme.</summary>
    protected override Type StyleKeyOverride => typeof(TextEditor);

    /// <summary>The program text.</summary>
    public string? Code
    {
        get => GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    /// <summary>The 1-based line highlighted as the current step, or null.</summary>
    public int? CurrentLine
    {
        get => GetValue(CurrentLineProperty);
        set => SetValue(CurrentLineProperty, value);
    }

    /// <summary>The line the highlighter currently paints, for tests.</summary>
    internal int? HighlightedLine => _highlighter.Line;

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CodeProperty)
            SyncTextFromCode(change.GetNewValue<string?>() ?? string.Empty);
        else if (change.Property == CurrentLineProperty)
            ShowCurrentLine(change.GetNewValue<int?>());
    }

    private void SyncTextFromCode(string code)
    {
        if (Text == code)
            return;
        _syncingFromProperty = true;
        try
        {
            Text = code;
        }
        finally
        {
            _syncingFromProperty = false;
        }
    }

    private void ShowCurrentLine(int? line)
    {
        _highlighter.Line = line;
        TextArea.TextView.InvalidateLayer(_highlighter.Layer);
        if (line is { } number && number >= 1 && number <= Document.LineCount)
            ScrollToLine(number);
    }

    /// <summary>Paints a full-width bar behind the current-step line.</summary>
    private sealed class CurrentLineHighlighter : IBackgroundRenderer
    {
        private static readonly IBrush Brush = new SolidColorBrush(Color.FromRgb(0x3d, 0x5d, 0x3d));

        public int? Line { get; set; }

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (Line is not { } number || textView.Document is not { } document
                || number < 1 || number > document.LineCount)
                return;
            textView.EnsureVisualLines();
            var line = document.GetLineByNumber(number);
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, line))
                drawingContext.FillRectangle(Brush, new Rect(0, rect.Top, textView.Bounds.Width, rect.Height));
        }
    }
}
