using CAP.Avalonia.Services.Localization;

namespace CAP.Avalonia.Services;

/// <summary>
/// A shipped example design: display name (file name without extension) and
/// the absolute path of its .lun file. Curated examples (listed in the
/// <c>examples.json</c> manifest) additionally carry their ladder level and
/// the localization key of a one-line description.
/// </summary>
/// <param name="Name">Display name shown on the Home screen.</param>
/// <param name="FilePath">Absolute path to the example's .lun file.</param>
/// <param name="DescriptionKey">Localization key of the one-line description, or null for uncurated examples.</param>
/// <param name="Level">Difficulty band of the ladder rung (Basics / Adders / Datapath), or null for uncurated examples.</param>
public record ExampleDesign(string Name, string FilePath, string? DescriptionKey = null, string? Level = null)
{
    /// <summary>
    /// Localized one-line description shown under the name on the Home screen;
    /// empty for uncurated examples. Resolved lazily so it follows the active
    /// UI language whenever the list is rebuilt.
    /// </summary>
    public string Description =>
        DescriptionKey == null ? "" : LocalizationService.Instance.Translate(DescriptionKey);

    /// <summary>Resource key of the tile icon on the Home screen, chosen from the name (see <see cref="ExampleIcon"/>).</summary>
    public string IconKey => ExampleIcon.For(Name);

    /// <summary>Tile caption on the Home screen (see <see cref="ExampleIcon.ShortName"/>).</summary>
    public string ShortName => ExampleIcon.ShortName(Name);
}

/// <summary>Projects discovered example metadata into localized Home-screen entries.</summary>
public class ExampleDesignsService(IExampleFileCatalog catalog)
{
    public IReadOnlyList<ExampleDesign> GetExamples() => catalog.GetExamples()
        .Select(entry => new ExampleDesign(entry.Name, entry.FilePath, entry.DescriptionKey, entry.Level)).ToList();

    public string? FindExamplesDirectory() => catalog.FindExamplesDirectory();
}
