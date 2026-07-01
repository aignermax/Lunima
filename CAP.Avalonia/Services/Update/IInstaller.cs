namespace CAP.Avalonia.Services.Update;

/// <summary>
/// Abstraction for performing an in-place self-update after the installer/archive
/// has been downloaded to a local file. Each platform provides its own implementation.
/// </summary>
public interface IInstaller
{
    /// <summary>
    /// Checks whether the downloaded artifact at <paramref name="downloadedPath"/> can be
    /// installed in-place from the current install location.
    /// Returns <c>false</c> (and a human-readable <paramref name="reason"/>) when the install
    /// directory is not writable, or when required tooling (e.g. <c>ditto</c>, <c>msiexec</c>)
    /// is not available — so the caller can fall back to opening the file manually.
    /// </summary>
    bool CanInstall(string downloadedPath, out string reason);

    /// <summary>
    /// Performs the in-place update: extracts / installs the artifact, swaps the old version
    /// with the new one, then relaunches into the updated application.
    /// After this method returns, the calling application should shut down.
    /// </summary>
    /// <param name="downloadedPath">Path to the downloaded installer artifact.</param>
    /// <param name="cancellationToken">Token to cancel the operation before launch.</param>
    Task InstallAndRelaunchAsync(string downloadedPath, CancellationToken cancellationToken = default);

    /// <summary>Discovers the install-directory path for the currently running application.</summary>
    string GetInstallDirectory();
}
