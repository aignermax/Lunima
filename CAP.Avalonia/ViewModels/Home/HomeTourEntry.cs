using System.Windows.Input;
using CAP.Avalonia.Services.Localization;

namespace CAP.Avalonia.ViewModels.Home;

/// <summary>
/// One row of the Home screen's "Learn Lunima" list: a guided tour with its
/// position in the learning order, icon and the localization keys of its name,
/// one-line description and tooltip. The command is the existing tour command
/// instance on <see cref="HomeViewModel"/>, so a list row starts exactly the
/// tour the previous standalone button started.
/// </summary>
/// <param name="Number">1-based position in the learning order, shown as the row's number.</param>
/// <param name="Icon">Emoji glyph shown before the name.</param>
/// <param name="NameKey">Localization key of the tour's display name.</param>
/// <param name="DescriptionKey">Localization key of the one-line description under the name.</param>
/// <param name="TipKey">Localization key of the row's tooltip.</param>
/// <param name="Command">The command that starts the tour.</param>
public record HomeTourEntry(
    int Number,
    string Icon,
    string NameKey,
    string DescriptionKey,
    string TipKey,
    ICommand Command)
{
    /// <summary>
    /// Localized display name. Resolved lazily so it follows the active UI
    /// language whenever the list is rebuilt (same pattern as
    /// <see cref="Services.ExampleDesign.Description"/>).
    /// </summary>
    public string Name => LocalizationService.Instance.Translate(NameKey);

    /// <summary>Localized one-line description shown under the name.</summary>
    public string Description => LocalizationService.Instance.Translate(DescriptionKey);

    /// <summary>Localized tooltip of the row.</summary>
    public string Tip => LocalizationService.Instance.Translate(TipKey);
}
