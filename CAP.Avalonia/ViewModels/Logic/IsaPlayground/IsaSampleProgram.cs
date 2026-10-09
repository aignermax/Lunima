using CAP.Avalonia.Services.Localization;

namespace CAP.Avalonia.ViewModels.Logic.IsaPlayground;

/// <summary>
/// A shipped ISA sample program: its file name, the localization key of its
/// display name, and the assembly source text loaded from disk.
/// </summary>
public sealed class IsaSampleProgram
{
    /// <summary>Initializes a new instance of <see cref="IsaSampleProgram"/>.</summary>
    /// <param name="fileName">File name inside <c>examples/isa/</c>.</param>
    /// <param name="displayNameKey">Localization key of the name shown in the sample picker.</param>
    /// <param name="source">The assembly source text.</param>
    public IsaSampleProgram(string fileName, string displayNameKey, string source)
    {
        FileName = fileName;
        DisplayNameKey = displayNameKey;
        Source = source;
    }

    /// <summary>File name inside <c>examples/isa/</c>.</summary>
    public string FileName { get; }

    /// <summary>Localization key of the name shown in the sample picker.</summary>
    public string DisplayNameKey { get; }

    /// <summary>The assembly source text.</summary>
    public string Source { get; }

    /// <summary>Localized display name; resolved lazily so it follows the active UI language.</summary>
    public string DisplayName => LocalizationService.Instance.Translate(DisplayNameKey);
}
