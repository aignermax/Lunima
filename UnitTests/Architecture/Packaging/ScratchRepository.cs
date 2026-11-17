namespace UnitTests.Architecture.Packaging;

/// <summary>A throw-away directory tree for the packaging-guard fixtures, deleted on dispose.</summary>
internal sealed class ScratchRepository : IDisposable
{
    /// <summary>Creates an empty directory under the system temp folder.</summary>
    public ScratchRepository()
    {
        Root = Path.Combine(Path.GetTempPath(), "lunima-packaging-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>The fixture's root directory.</summary>
    public string Root { get; }

    /// <summary>Writes <paramref name="content"/> to the root-relative <paramref name="relativePath"/>.</summary>
    public string Write(string relativePath, string content = "")
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(Root, recursive: true);
}
