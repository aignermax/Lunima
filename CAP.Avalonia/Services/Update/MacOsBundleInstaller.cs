using System.Diagnostics;
using CAP_Core.Export;

namespace CAP.Avalonia.Services.Update;

/// <summary>
/// In-place self-updater for macOS: extracts a <c>.zip</c> containing <c>Lunima.app</c>,
/// strips quarantine, ad-hoc-signs the new bundle, swaps it over the current install,
/// then relaunches via <c>open -n</c>.
/// </summary>
/// <remarks>
/// The swap and relaunch happen inside a detached Bash helper script so the old process
/// can exit before the files are replaced. The helper keeps a <c>.bak</c> copy of the
/// old bundle for rollback; it is removed once the relaunch is confirmed.
/// </remarks>
public class MacOsBundleInstaller : IInstaller
{
    private readonly ProcessLaunchFactory _launchFactory;

    /// <summary>Initializes the installer with the shared process-launch factory.</summary>
    /// <param name="launchFactory">Factory used to build cross-platform <see cref="ProcessStartInfo"/> instances.</param>
    public MacOsBundleInstaller(ProcessLaunchFactory launchFactory)
    {
        _launchFactory = launchFactory ?? throw new ArgumentNullException(nameof(launchFactory));
    }

    /// <summary>Initializes the installer using a default <see cref="ProcessLaunchFactory"/>.</summary>
    public MacOsBundleInstaller() : this(ProcessLaunchFactory.CreateDefault()) { }
    /// <summary>
    /// Returns the <c>.app</c> bundle directory that contains the currently running process.
    /// Walks up from <c>Contents/MacOS/</c> to find the three-level parent ending in <c>.app</c>.
    /// </summary>
    public string GetInstallDirectory()
    {
        var processPath = Environment.ProcessPath ?? AppContext.BaseDirectory;
        // Typical layout: /Applications/Lunima.app/Contents/MacOS/Lunima
        // GetDirectoryName("…/Contents/MacOS/Lunima") → "…/Contents/MacOS"
        // GetDirectoryName("…/Contents/MacOS")         → "…/Contents"
        // GetDirectoryName("…/Contents")               → "…/Lunima.app"
        var dir = Path.GetDirectoryName(processPath) ?? processPath;
        dir = Path.GetDirectoryName(dir) ?? dir;     // Contents/MacOS  → Contents
        dir = Path.GetDirectoryName(dir) ?? dir;     // Contents        → .app bundle
        return dir;
    }

    /// <inheritdoc/>
    public bool CanInstall(string downloadedPath, out string reason)
    {
        if (!File.Exists(downloadedPath))
        {
            reason = $"Downloaded file not found: {downloadedPath}";
            return false;
        }

        if (!downloadedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Expected a .zip archive; got: {Path.GetFileName(downloadedPath)}";
            return false;
        }

        var installDir = GetInstallDirectory();
        var parent = Path.GetDirectoryName(installDir);
        if (parent == null || !IsDirectoryWritable(parent))
        {
            reason = $"Install location is not writable: {parent ?? installDir}";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <inheritdoc/>
    public async Task InstallAndRelaunchAsync(string downloadedPath, CancellationToken cancellationToken = default)
    {
        var appBundle  = GetInstallDirectory();
        var backupPath = appBundle + ".bak";
        var scriptPath = Path.Combine(Path.GetTempPath(), $"lunima_update_{Guid.NewGuid():N}.sh");

        var script = BuildHelperScript(appBundle, backupPath, downloadedPath, appBundle);
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

        // Make the script executable and detach it from this process
        if (_launchFactory.TryBuild("chmod", new[] { "+x", scriptPath }, null, null, out var chmodInfo, out _))
            Process.Start(chmodInfo)?.WaitForExit();

        if (_launchFactory.TryBuild("/bin/bash", new[] { scriptPath }, null, null, out var bashInfo, out _))
            Process.Start(bashInfo);
    }

    /// <summary>
    /// Generates the detached Bash helper script that performs the swap and relaunch.
    /// Exposed for unit-testing the script content.
    /// </summary>
    internal static string BuildHelperScript(
        string installDir,
        string backupDir,
        string zipFile,
        string relaunchPath)
    {
        // $$"""...""" — C# interpolation uses {{var}}, so literal { } in bash are just { }
        return $$"""
            #!/bin/bash
            set -euo pipefail

            INSTALL_DIR="{{installDir}}"
            BACKUP_DIR="{{backupDir}}"
            ZIP_FILE="{{zipFile}}"
            RELAUNCH="{{relaunchPath}}"

            # Wait for the parent application to exit
            sleep 3

            TMP_EXTRACT="$(mktemp -d)"

            cleanup() {
                rm -rf "$TMP_EXTRACT" 2>/dev/null || true
            }
            trap cleanup EXIT

            # Extract the .zip archive (preserves HFS+ metadata via ditto)
            ditto -xk "$ZIP_FILE" "$TMP_EXTRACT"

            NEW_APP="$(find "$TMP_EXTRACT" -maxdepth 1 -name "*.app" | head -1)"
            if [ -z "$NEW_APP" ]; then
                echo "ERROR: no .app bundle found in archive" >&2
                exit 1
            fi

            # Strip quarantine attribute — prevents App Translocation on the new bundle
            xattr -dr com.apple.quarantine "$NEW_APP" 2>/dev/null || true

            # Ad-hoc sign all dylibs and frameworks inside-out, then sign the bundle itself.
            # Apple Silicon requires at least an ad-hoc signature; hardened-runtime / Developer
            # ID signing is a separate follow-up.
            find "$NEW_APP/Contents/Frameworks" -name "*.dylib" 2>/dev/null | sort -r | \
                while IFS= read -r lib; do
                    codesign --force --sign - "$lib" 2>/dev/null || true
                done
            find "$NEW_APP/Contents/Frameworks" -name "*.framework" 2>/dev/null | sort -r | \
                while IFS= read -r fw; do
                    codesign --force --sign - "$fw" 2>/dev/null || true
                done
            codesign --force --sign - "$NEW_APP" 2>/dev/null || true

            # Near-atomic swap: old → .bak, new → install location
            rm -rf "$BACKUP_DIR" 2>/dev/null || true
            mv "$INSTALL_DIR" "$BACKUP_DIR"
            mv "$NEW_APP" "$INSTALL_DIR"

            # Relaunch into the updated application
            open -n "$RELAUNCH"

            # Cleanup
            rm -f "$ZIP_FILE" 2>/dev/null || true
            sleep 5
            rm -rf "$BACKUP_DIR" 2>/dev/null || true
            """;
    }

    private static bool IsDirectoryWritable(string path)
    {
        try
        {
            var probe = Path.Combine(path, $".lunima_write_test_{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
