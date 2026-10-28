namespace CAP.Avalonia.Services;

/// <summary>Unlocalized metadata discovered beside shipped design files.</summary>
public sealed record ExampleFileEntry(string Name, string FilePath, string? DescriptionKey = null,
    string? Level = null);

/// <summary>Discovers shipped examples without exposing filesystem access to presentation.</summary>
public interface IExampleFileCatalog
{
    IReadOnlyList<ExampleFileEntry> GetExamples();
    string? FindExamplesDirectory();
}
