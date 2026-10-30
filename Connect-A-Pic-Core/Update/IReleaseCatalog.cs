namespace CAP_Core.Update;

/// <summary>Reads available publisher releases without exposing transport to presentation.</summary>
public interface IReleaseCatalog
{
    Task<GitHubReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken = default);
}
