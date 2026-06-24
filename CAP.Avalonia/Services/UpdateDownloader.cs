using System.Diagnostics;
using System.Net.Http;
using CAP_Core.Update;

namespace CAP.Avalonia.Services;

/// <summary>
/// Downloads an MSI installer from a GitHub release asset URL with progress reporting.
/// </summary>
public class UpdateDownloader
{
    private readonly HttpClient _httpClient;

    /// <summary>Initializes a new instance of <see cref="UpdateDownloader"/>.</summary>
    /// <param name="httpClient">HTTP client used for downloading.</param>
    public UpdateDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Downloads the installer from <paramref name="downloadUrl"/> to a temporary file,
    /// reporting fractional progress (0.0–1.0) via <paramref name="progress"/>.
    /// The temporary file extension is derived from the asset filename in <paramref name="downloadUrl"/>.
    /// </summary>
    /// <returns>Local file path to the downloaded installer.</returns>
    public async Task<string> DownloadMsiAsync(
        string downloadUrl,
        long expectedSize,
        IProgress<double> progress,
        CancellationToken cancellationToken = default)
    {
        var assetFileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
        var extension = DeriveInstallerExtension(assetFileName);
        var tempPath = Path.Combine(Path.GetTempPath(), $"Lunima_Update_{Guid.NewGuid():N}{extension}");

        using var response = await _httpClient.GetAsync(
            downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? expectedSize;
        using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var destStream = new FileStream(
            tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long bytesRead = 0;
        int count;

        while ((count = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destStream.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            bytesRead += count;

            if (totalBytes > 0)
                progress.Report((double)bytesRead / totalBytes);
        }

        return tempPath;
    }

    /// <summary>
    /// Launches the downloaded installer.
    /// <list type="bullet">
    ///   <item><description>Windows — invokes <c>msiexec /i &lt;path&gt;</c> via <see cref="ProcessStartInfo.ArgumentList"/>.</description></item>
    ///   <item><description>macOS — opens the <c>.dmg</c> or <c>.pkg</c> with <c>open</c> for manual installation.</description></item>
    ///   <item><description>Other platforms — no-op (caller should redirect to the releases page).</description></item>
    /// </list>
    /// </summary>
    /// <param name="installerPath">Full path to the downloaded installer file.</param>
    public static void LaunchInstaller(string installerPath)
    {
        if (OperatingSystem.IsWindows())
        {
            var psi = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                UseShellExecute = true,
            };
            psi.ArgumentList.Add("/i");
            psi.ArgumentList.Add(installerPath);
            Process.Start(psi);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            // Open the .dmg / .pkg for the user — they complete the install manually.
            var psi = new ProcessStartInfo
            {
                FileName = "open",
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(installerPath);
            Process.Start(psi);
            return;
        }

        // Linux and other platforms: no automated installer launch — caller should open browser.
    }

    /// <summary>
    /// Returns the file extension to use for a downloaded installer asset,
    /// preserving compound extensions such as <c>.tar.gz</c>.
    /// Falls back to <c>.msi</c> when the filename cannot be parsed.
    /// </summary>
    internal static string DeriveInstallerExtension(string assetFileName)
    {
        if (string.IsNullOrEmpty(assetFileName))
            return ".msi";

        // Preserve compound extension .tar.gz
        if (assetFileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            return ".tar.gz";

        var ext = Path.GetExtension(assetFileName);
        return string.IsNullOrEmpty(ext) ? ".msi" : ext;
    }
}
