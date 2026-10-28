using System.Runtime.InteropServices;

namespace CAP_Core.Update;

/// <summary>Pure release-version and platform-asset selection policy.</summary>
public static class ReleaseAssetPolicy
{
    /// <summary>
    /// Returns true if <paramref name="release"/> has a version newer than <paramref name="currentVersion"/>.
    /// </summary>
    public static bool IsNewerThan(GitHubReleaseInfo release, SemanticVersion currentVersion)
    {
        var releaseVersion = release.ParsedVersion;
        return releaseVersion != null && releaseVersion > currentVersion;
    }

    /// <summary>
    /// Finds the first MSI installer asset in the release's asset list.
    /// Returns null if no MSI asset exists.
    /// </summary>
    /// <remarks>
    /// This method always matches <c>.msi</c> regardless of the current platform.
    /// Use <see cref="FindPlatformAsset"/> when you want platform-aware selection.
    /// </remarks>
    public static GitHubReleaseAsset? FindMsiAsset(GitHubReleaseInfo release)
    {
        return release.Assets.FirstOrDefault(a =>
            a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds the best installer asset for the current operating system.
    /// <list type="bullet">
    ///   <item><description>Windows — <c>.msi</c></description></item>
    ///   <item><description>macOS — <c>.dmg</c>, then <c>.pkg</c></description></item>
    ///   <item><description>Linux — <c>.tar.gz</c>, then <c>.AppImage</c></description></item>
    /// </list>
    /// Returns null if no matching asset exists for the current platform.
    /// </summary>
    public static GitHubReleaseAsset? FindPlatformAsset(GitHubReleaseInfo release)
    {
        if (OperatingSystem.IsWindows())
            return release.Assets.FirstOrDefault(a =>
                a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));

        if (OperatingSystem.IsMacOS())
        {
            // Prefer the .dmg matching the current arch (both osx-arm64 and osx-x64 ship together),
            // then fall back to any .dmg/.pkg so an unusual naming still yields something.
            var rid = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
            return release.Assets.FirstOrDefault(a =>
                    a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase) &&
                    a.Name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
                ?? release.Assets.FirstOrDefault(a =>
                    a.Name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
                ?? release.Assets.FirstOrDefault(a =>
                    a.Name.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase));
        }

        // Linux
        return release.Assets.FirstOrDefault(a =>
            a.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            ?? release.Assets.FirstOrDefault(a =>
            a.Name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds the release asset used for an <b>in-place auto-update</b> on the current OS/arch:
    /// the arch-specific zipped <c>.app</c> on macOS, the <c>.msi</c> on Windows, the
    /// <c>.tar.gz</c> on Linux. Distinct from <see cref="FindPlatformAsset"/>, which selects the
    /// artifact for a first-time manual install (the macOS <c>.dmg</c>). Returns null when the
    /// release carries no suitable auto-update archive (e.g. an older release without the zip),
    /// so the caller can fall back to the manual installer.
    /// </summary>
    public static GitHubReleaseAsset? FindAutoUpdateAsset(GitHubReleaseInfo release)
    {
        if (OperatingSystem.IsWindows())
            // SelfUpdateInstaller first verifies this process runs from the registered MSI root.
            return release.Assets.FirstOrDefault(a => a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));

        if (OperatingSystem.IsMacOS())
        {
            var rid = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
            return release.Assets.FirstOrDefault(a =>
                a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }

        // Linux
        return release.Assets.FirstOrDefault(a =>
            a.Name.Contains("linux", StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase));
    }

}
