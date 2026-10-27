namespace CAP_DataAccess.Components.ComponentDraftMapper;

/// <summary>Discovers shipped PDK files and identifies their read-only source boundary.</summary>
public interface IBundledPdkCatalog
{
    BundledPdkInventory? ReadInventory();
    bool IsBundledFile(string filePath);
}

/// <summary>A directory and its enumerated files; null inventory means no bundled directory exists.</summary>
public sealed record BundledPdkInventory(string Directory, IReadOnlyList<string> Files);
