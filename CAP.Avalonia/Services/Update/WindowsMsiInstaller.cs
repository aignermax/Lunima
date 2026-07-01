using System.Diagnostics;
using CAP_Core.Export;

namespace CAP.Avalonia.Services.Update;

/// <summary>
/// In-place self-updater for Windows: runs the MSI installer silently via <c>msiexec</c>,
/// then relaunches the updated application. A detached PowerShell helper handles the
/// wait-and-relaunch so the original process can exit before the MSI replaces files.
/// </summary>
public class WindowsMsiInstaller : IInstaller
{
    private readonly ProcessLaunchFactory _launchFactory;

    /// <summary>Initializes the installer with the shared process-launch factory.</summary>
    /// <param name="launchFactory">Factory used to build cross-platform <see cref="ProcessStartInfo"/> instances.</param>
    public WindowsMsiInstaller(ProcessLaunchFactory launchFactory)
    {
        _launchFactory = launchFactory ?? throw new ArgumentNullException(nameof(launchFactory));
    }

    /// <summary>Initializes the installer using a default <see cref="ProcessLaunchFactory"/>.</summary>
    public WindowsMsiInstaller() : this(ProcessLaunchFactory.CreateDefault()) { }
    /// <summary>
    /// Returns the directory that contains the currently running <c>Lunima.exe</c>.
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

        if (!downloadedPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Expected a .msi installer; got: {Path.GetFileName(downloadedPath)}";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <inheritdoc/>
    public async Task InstallAndRelaunchAsync(string downloadedPath, CancellationToken cancellationToken = default)
    {
        var relaunchPath = Environment.ProcessPath ?? string.Empty;
        var scriptPath   = Path.Combine(Path.GetTempPath(), $"lunima_update_{Guid.NewGuid():N}.ps1");

        var script = BuildHelperScript(downloadedPath, relaunchPath);
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

        // Launch PowerShell detached so it survives this process exiting
        if (_launchFactory.TryBuild("powershell.exe",
                new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath },
                null, null, out var psInfo, out _))
            Process.Start(psInfo);
    }

    /// <summary>
    /// Generates the detached PowerShell helper script.
    /// Exposed for unit-testing the script content.
    /// </summary>
    internal static string BuildHelperScript(string msiPath, string relaunchPath)
    {
        // Use regular string interpolation — no bash braces to worry about here
        return
            "# Lunima auto-update helper — generated, do not edit\n" +
            "param()\n\n" +
            "# Wait for the parent application to exit\n" +
            "Start-Sleep -Seconds 3\n\n" +
            $"$msiPath      = \"{msiPath.Replace("\"", "`\"")}\"\n" +
            $"$relaunchPath = \"{relaunchPath.Replace("\"", "`\"")}\"\n\n" +
            "# Run msiexec in a reduced UI mode (shows progress bar, no reboot prompt)\n" +
            "$proc = Start-Process -FilePath \"msiexec.exe\" `\n" +
            "            -ArgumentList \"/i `\"$msiPath`\" /qb /norestart\" `\n" +
            "            -Wait -PassThru\n\n" +
            "if ($proc.ExitCode -eq 0 -and (Test-Path $relaunchPath)) {\n" +
            "    Start-Process -FilePath $relaunchPath\n" +
            "}\n\n" +
            "# Cleanup\n" +
            "Remove-Item -Path $msiPath -Force -ErrorAction SilentlyContinue\n" +
            "Remove-Item -Path $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue\n";
    }
}
