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
    /// Downloads the MSI from <paramref name="downloadUrl"/> to a temporary file,
    /// reporting fractional progress (0.0–1.0) via <paramref name="progress"/>.
    /// </summary>
    /// <returns>Local file path to the downloaded MSI.</returns>
    public async Task<string> DownloadMsiAsync(
        string downloadUrl,
        long expectedSize,
        IProgress<double> progress,
        CancellationToken cancellationToken = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"Lunima_Update_{Guid.NewGuid():N}.msi");

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
    /// Launches the MSI installer via msiexec (Windows only).
    /// Does nothing on non-Windows platforms.
    /// </summary>
    /// <param name="msiPath">Full path to the downloaded MSI file.</param>
    public static void LaunchInstaller(string msiPath)
    {
        if (!OperatingSystem.IsWindows())
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "msiexec.exe",
            Arguments = $"/i \"{msiPath}\"",
            UseShellExecute = true,
        });
    }
}
