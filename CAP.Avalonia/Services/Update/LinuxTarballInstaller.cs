using System.Diagnostics;

namespace CAP.Avalonia.Services.Update;

/// <summary>
/// In-place self-updater for Linux: extracts the <c>.tar.gz</c> portable archive over the
/// current install directory via a detached Bash helper, then relaunches the application.
/// </summary>
public class LinuxTarballInstaller : IInstaller
{
    /// <summary>
    /// Returns the directory that contains the currently running <c>Lunima</c> executable.
    /// </summary>
    public string GetInstallDirectory()
    {
        var processPath = Environment.ProcessPath ?? AppContext.BaseDirectory;
        return Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory;
    }

    /// <inheritdoc/>
    public bool CanInstall(string downloadedPath, out string reason)
    {
        if (!File.Exists(downloadedPath))
        {
            reason = $"Downloaded file not found: {downloadedPath}";
            return false;
        }

        if (!downloadedPath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Expected a .tar.gz archive; got: {Path.GetFileName(downloadedPath)}";
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
        var installDir  = GetInstallDirectory();
        var backupDir   = installDir + ".bak";
        var relaunchExe = Environment.ProcessPath ?? Path.Combine(installDir, "Lunima");
        var scriptPath  = Path.Combine(Path.GetTempPath(), $"lunima_update_{Guid.NewGuid():N}.sh");

        var script = BuildHelperScript(installDir, backupDir, downloadedPath, relaunchExe);
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

        Process.Start(new ProcessStartInfo
        {
            FileName         = "chmod",
            ArgumentList     = { "+x", scriptPath },
            UseShellExecute  = false,
            CreateNoWindow   = true,
        })?.WaitForExit();

        Process.Start(new ProcessStartInfo
        {
            FileName         = "/bin/bash",
            ArgumentList     = { scriptPath },
            UseShellExecute  = false,
            CreateNoWindow   = true,
        });
    }

    /// <summary>
    /// Generates the detached Bash helper script.
    /// Exposed for unit-testing the script content.
    /// </summary>
    internal static string BuildHelperScript(
        string installDir,
        string backupDir,
        string tarFile,
        string relaunchExe)
    {
        // $$"""...""" — C# interpolation uses {{var}}, so literal { } in bash are just { }
        return $$"""
            #!/bin/bash
            set -euo pipefail

            INSTALL_DIR="{{installDir}}"
            BACKUP_DIR="{{backupDir}}"
            TAR_FILE="{{tarFile}}"
            RELAUNCH="{{relaunchExe}}"

            # Wait for the parent application to exit
            sleep 3

            TMP_EXTRACT="$(mktemp -d)"

            cleanup() {
                rm -rf "$TMP_EXTRACT" 2>/dev/null || true
            }
            trap cleanup EXIT

            # Extract the archive
            tar -xzf "$TAR_FILE" -C "$TMP_EXTRACT"

            # Near-atomic swap: old → .bak, extracted content → install location
            rm -rf "$BACKUP_DIR" 2>/dev/null || true
            mv "$INSTALL_DIR" "$BACKUP_DIR"
            mv "$TMP_EXTRACT" "$INSTALL_DIR"

            # Ensure the main executable is runnable
            chmod +x "$INSTALL_DIR/Lunima" 2>/dev/null || true

            # Relaunch
            "$RELAUNCH" &

            # Cleanup
            rm -f "$TAR_FILE" 2>/dev/null || true
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
