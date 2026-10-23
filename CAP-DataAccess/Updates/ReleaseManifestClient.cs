using CAP_Core.Update;

namespace CAP_DataAccess.Updates;

/// <summary>Fetches a bounded publisher envelope and resolves a release's authenticated artifact.</summary>
public sealed class ReleaseManifestClient(HttpClient client, ReleaseManifestVerifier verifier)
{
    public const string AssetName = "release-manifest.json";

    public async Task<VerifiedReleaseArtifact> ResolveAsync(GitHubReleaseInfo release,
        GitHubReleaseAsset asset, SemanticVersion currentVersion, string runtimeIdentifier,
        CancellationToken cancellationToken = default)
    {
        var manifests = release.Assets.Where(candidate => candidate.Name == AssetName).ToArray();
        if (manifests.Length != 1)
            throw new InvalidDataException("This release does not include one publisher manifest. Use the manual release page.");
        if (!Uri.TryCreate(manifests[0].BrowserDownloadUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("The publisher manifest requires an HTTPS URL.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(1));
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > ReleaseManifestVerifier.MaximumEnvelopeBytes)
                throw new InvalidDataException("The publisher manifest is too large.");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
            {
                if (bytes.Length + count > ReleaseManifestVerifier.MaximumEnvelopeBytes)
                    throw new InvalidDataException("The publisher manifest is too large.");
                bytes.Write(buffer, 0, count);
            }
            deadline.Token.ThrowIfCancellationRequested();
            return verifier.Verify(bytes.ToArray(), release.TagName, currentVersion, runtimeIdentifier, asset.Name);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The publisher manifest request timed out.", error);
        }
    }
}
