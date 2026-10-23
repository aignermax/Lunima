using System.Net.Http;
using System.Runtime.InteropServices;
using CAP_Core.Update;

namespace CAP_DataAccess.Updates;

/// <summary>Resolves authenticated release metadata before downloading an installer.</summary>
public sealed class UpdateDownloader : IUpdatePreparation
{
    private readonly ReleaseManifestClient _manifests;
    private readonly VerifiedReleaseDownloader _downloads;

    public UpdateDownloader(HttpClient httpClient, ReleaseManifestVerifier? verifier = null, string? stagingRoot = null)
    {
        _manifests = new ReleaseManifestClient(httpClient, verifier ?? new ReleaseManifestVerifier(ReleasePublisherKeys.All));
        _downloads = new VerifiedReleaseDownloader(httpClient,
            stagingRoot ?? Path.Combine(Path.GetTempPath(), "Lunima", "Updates"));
    }

    public async Task<IUpdateDownload> DownloadInstallerAsync(GitHubReleaseInfo release,
        GitHubReleaseAsset asset, SemanticVersion currentVersion, IProgress<double> progress,
        CancellationToken cancellationToken = default)
    {
        var authenticated = await _manifests.ResolveAsync(release, asset, currentVersion,
            CurrentRuntimeIdentifier(), cancellationToken).ConfigureAwait(false);
        if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var url))
            throw new InvalidDataException("The release artifact has an invalid URL.");
        return await _downloads.DownloadAsync(url, authenticated, progress, cancellationToken).ConfigureAwait(false);
    }

    internal static string CurrentRuntimeIdentifier()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64", Architecture.Arm64 => "arm64",
            _ => throw new InvalidDataException("Use a manual download for this processor architecture.")
        };
        var platform = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx"
            : OperatingSystem.IsLinux() ? "linux" : throw new InvalidDataException("Use a manual download on this platform.");
        return platform + "-" + architecture;
    }
}
