namespace CAP_Core.Update;

/// <summary>Authenticates and stages one platform update without exposing network or file IO to the UI.</summary>
public interface IUpdatePreparation
{
    Task<IUpdateDownload> DownloadInstallerAsync(GitHubReleaseInfo release, GitHubReleaseAsset asset,
        SemanticVersion currentVersion, IProgress<double> progress, CancellationToken cancellationToken = default);
}

/// <summary>Owns the staged file until a successful installer launch takes ownership.</summary>
public interface IUpdateDownload : IDisposable
{
    string Path { get; }
    Task ValidateAsync(CancellationToken cancellationToken = default);
    void HandOffToInstaller();
}
