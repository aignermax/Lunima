namespace CAP_Core.Update;

/// <summary>
/// Represents a GitHub release with version metadata, release notes, and asset download URLs.
/// </summary>
public sealed class GitHubReleaseInfo
{
    /// <summary>Gets the release tag (e.g. "v1.2.3").</summary>
    public string TagName { get; init; } = "";

    /// <summary>Gets the release title.</summary>
    public string Name { get; init; } = "";

    /// <summary>Gets the release notes (Markdown body).</summary>
    public string Body { get; init; } = "";

    /// <summary>Gets whether this is a pre-release.</summary>
    public bool IsPrerelease { get; init; }

    /// <summary>Gets when the release was published.</summary>
    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>Gets the downloadable assets attached to this release.</summary>
    public List<GitHubReleaseAsset> Assets { get; init; } = new();

    /// <summary>
    /// Parses the tag name as a <see cref="SemanticVersion"/>.
    /// Returns null if the tag name is not a valid semantic version.
    /// </summary>
    public SemanticVersion? ParsedVersion =>
        SemanticVersion.TryParse(TagName, out var v) ? v : null;
}

/// <summary>
/// Represents a single downloadable asset attached to a GitHub release.
/// </summary>
public sealed class GitHubReleaseAsset
{
    /// <summary>Gets the asset file name (e.g. "Lunima-1.2.3.msi").</summary>
    public string Name { get; init; } = "";

    /// <summary>Gets the direct browser download URL for the asset.</summary>
    public string BrowserDownloadUrl { get; init; } = "";

    /// <summary>Gets the file size in bytes.</summary>
    public long Size { get; init; }

    /// <summary>Gets the MIME content type of the asset.</summary>
    public string ContentType { get; init; } = "";
}
