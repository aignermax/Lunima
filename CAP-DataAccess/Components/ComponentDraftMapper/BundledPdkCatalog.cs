namespace CAP_DataAccess.Components.ComponentDraftMapper;

/// <summary>Bundled PDK discovery and write-boundary policy for one application base directory.</summary>
public sealed class BundledPdkCatalog(string baseDirectory) : IBundledPdkCatalog
{
    public BundledPdkInventory? ReadInventory()
    {
        var directory = BundledPdkPaths.ResolveBundledPdkDirectory(baseDirectory);
        return directory is null ? null : new BundledPdkInventory(directory,
            Array.AsReadOnly(Directory.GetFiles(directory, "*.json")));
    }

    public bool IsBundledFile(string filePath) => BundledPdkPaths.IsBundledPdkFile(filePath, baseDirectory);
}
