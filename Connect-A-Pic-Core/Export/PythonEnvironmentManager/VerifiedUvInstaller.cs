using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

namespace CAP_Core.Export.PythonEnvironmentManager;

/// <summary>Installs a managed executable only from a verified pinned archive.</summary>
internal sealed class VerifiedUvInstaller(HttpClient client, string toolsDirectory, TimeSpan? downloadTimeout = null)
{
    // Setup is infrequent. One process-wide owner avoids duplicate downloads without
    // retaining a growing dictionary of locks keyed by test/user environment paths.
    private static readonly SemaphoreSlim Installation = new(1, 1);
    private const long MaximumArchiveBytes = 128L * 1024 * 1024;
    private const long MaximumBinaryBytes = 256L * 1024 * 1024;

    public async Task<string> EnsureAsync(UvReleaseArtifact artifact, IProgress<string>? progress, CancellationToken token)
    {
        await Installation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var directory = Path.Combine(toolsDirectory, "uv", artifact.Version, artifact.Platform);
            Directory.CreateDirectory(directory);
            var archive = Path.Combine(directory, artifact.FileName);
            if (!await MatchesHashAsync(archive, artifact.Sha256, token).ConfigureAwait(false))
            {
                File.Delete(archive);
                await DownloadAsync(artifact, archive, progress, token).ConfigureAwait(false);
            }

            var binary = Path.Combine(directory, artifact.ExecutableName);
            var staging = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                // Read the binary from the verified archive even on cache hits. A
                // self-reported sidecar hash is not evidence that cached bytes are trusted.
                await ExtractAsync(archive, artifact, staging, token).ConfigureAwait(false);
                var expectedBinaryHash = await HashAsync(staging, token).ConfigureAwait(false);
                if (!await MatchesHashAsync(binary, expectedBinaryHash, token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();
                    File.Move(staging, binary, overwrite: true);
                }
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(binary, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                        UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                return binary;
            }
            finally { DeleteStaging(staging); }
        }
        finally { Installation.Release(); }
    }

    private async Task DownloadAsync(UvReleaseArtifact artifact, string archive,
        IProgress<string>? progress, CancellationToken token)
    {
        var staging = archive + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(downloadTimeout ?? TimeSpan.FromMinutes(10));
        var downloadToken = deadline.Token;
        try
        {
            progress?.Report($"Downloading verified uv {artifact.Version}...");
            await using (var input = await client.GetStreamAsync(artifact.Url, downloadToken).ConfigureAwait(false))
            await using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, useAsync: true))
                await CopyBoundedAsync(input, output, MaximumArchiveBytes, downloadToken).ConfigureAwait(false);

            if (!await MatchesHashAsync(staging, artifact.Sha256, downloadToken).ConfigureAwait(false))
                throw new InvalidDataException("The uv download did not match its reviewed SHA-256 checksum.");
            downloadToken.ThrowIfCancellationRequested();
            File.Move(staging, archive, overwrite: true);
        }
        catch (OperationCanceledException error) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
        {
            throw new TimeoutException("The uv download exceeded its time limit.", error);
        }
        finally { DeleteStaging(staging); }
    }

    private static async Task ExtractAsync(string archivePath, UvReleaseArtifact artifact,
        string destination, CancellationToken token)
    {
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 81920, useAsync: true);
        if (artifact.FileName.EndsWith(".zip", StringComparison.Ordinal))
        {
            using var zip = ZipFile.OpenRead(archivePath);
            var entry = zip.Entries.FirstOrDefault(e => e.Name == artifact.ExecutableName)
                ?? throw new InvalidDataException("The verified uv archive contains no executable.");
            await using var input = entry.Open();
            await CopyBoundedAsync(input, output, MaximumBinaryBytes, token).ConfigureAwait(false);
            return;
        }

        await using var file = File.OpenRead(archivePath);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        await using var tar = new TarReader(gzip);
        while (await tar.GetNextEntryAsync(cancellationToken: token).ConfigureAwait(false) is { } entry)
        {
            if (Path.GetFileName(entry.Name) != artifact.ExecutableName ||
                entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream == null)
                continue;
            await CopyBoundedAsync(entry.DataStream, output, MaximumBinaryBytes, token).ConfigureAwait(false);
            return;
        }
        throw new InvalidDataException("The verified uv archive contains no executable.");
    }

    private static async Task CopyBoundedAsync(Stream input, Stream output, long maximum, CancellationToken token)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            total += read;
            if (total > maximum) throw new InvalidDataException("The uv artifact exceeds its allowed size.");
            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
    }

    private static async Task<bool> MatchesHashAsync(string path, string expected, CancellationToken token) =>
        File.Exists(path) && string.Equals(await HashAsync(path, token).ConfigureAwait(false), expected, StringComparison.OrdinalIgnoreCase);

    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
    }

    private static void DeleteStaging(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* Another reader/antivirus may temporarily hold the owned staging file. */ }
        catch (UnauthorizedAccessException) { /* Cleanup must not hide verification/cancellation errors. */ }
    }
}
