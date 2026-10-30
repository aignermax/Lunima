using CAP_DataAccess.Components.ComponentDraftMapper;

namespace UnitTests.Helpers;

internal sealed class TestBundledPdkCatalog(Func<BundledPdkInventory?> read, Func<string, bool>? classify = null)
    : IBundledPdkCatalog
{
    public BundledPdkInventory? ReadInventory() => read();
    public bool IsBundledFile(string path) => classify?.Invoke(path) ?? false;

    internal static TestBundledPdkCatalog ForDirectory(string? directory) => new(
        () => directory is null || !Directory.Exists(directory) ? null : new(directory, Directory.GetFiles(directory, "*.json")),
        path => directory is not null && string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)),
            Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase));
}
