using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Logic.IsaPlayground;

/// <summary>
/// One row of the program trace listing: a 1-based source line number, the raw
/// source text of that line, and whether the program counter currently points
/// at the instruction this line produced.
/// </summary>
public partial class IsaTraceLineViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>Initializes a new instance of <see cref="IsaTraceLineViewModel"/>.</summary>
    /// <param name="lineNumber">The 1-based source line number (matches the editor).</param>
    /// <param name="text">The raw source text of the line, trailing whitespace trimmed.</param>
    public IsaTraceLineViewModel(int lineNumber, string text)
    {
        LineNumber = lineNumber;
        Text = text;
    }

    /// <summary>The 1-based source line number (matches the editor).</summary>
    public int LineNumber { get; }

    /// <summary>The raw source text of the line, trailing whitespace trimmed.</summary>
    public string Text { get; }
}
