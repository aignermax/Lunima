using System.Security.Cryptography;
using CAP_Core.Update;

namespace CAP_DataAccess.Updates;

/// <summary>Downloads only bytes whose identity was authenticated by the publisher manifest.</summary>
public sealed class VerifiedReleaseDownloader(HttpClient client, string stagingRoot, TimeSpan? timeout = null)
{
    public async Task<VerifiedUpdateDownload> DownloadAsync(Uri url, VerifiedReleaseArtifact artifact,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (url.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Updates require an HTTPS download URL.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromMinutes(15));
        var directory = System.IO.Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        var path = System.IO.Path.Combine(directory, artifact.Name);
        var completed = false;
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } size && size != artifact.Size)
                throw new InvalidDataException("The download length does not match the signed artifact.");
            Directory.CreateDirectory(directory);
            await CopyVerifiedAsync(response.Content, path, artifact, progress, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            completed = true;
            return new VerifiedUpdateDownload(path, artifact);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The update download exceeded its time limit.", error);
        }
        finally
        {
            if (!completed) VerifiedUpdateDownload.DeleteOwnedFile(path);
        }
    }

    private static async Task CopyVerifiedAsync(HttpContent content, string path,
        VerifiedReleaseArtifact artifact, IProgress<double>? progress, CancellationToken token)
    {
        await using var input = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long received = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            received += count;
            if (received > artifact.Size) throw new InvalidDataException("The update exceeds its signed size.");
            hash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            progress?.Report((double)received / artifact.Size);
        }
        if (received != artifact.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(artifact.Sha256)))
            throw new InvalidDataException("The downloaded update does not match its signed identity.");
    }
}

/// <summary>Owns a verified temporary artifact until it is handed to the installer.</summary>
public sealed class VerifiedUpdateDownload : IUpdateDownload
{
    private int _ownership; // 0 = owned, 1 = disposed, 2 = transferred.
    internal VerifiedUpdateDownload(string path, VerifiedReleaseArtifact artifact) => (Path, Artifact) = (path, artifact);
    public string Path { get; }
    public VerifiedReleaseArtifact Artifact { get; }
    public async Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _ownership) != 0) throw new ObjectDisposedException(nameof(VerifiedUpdateDownload));
        await using var file = new FileStream(Path, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete, 81920, useAsync: true);
        if (file.Length != Artifact.Size)
            throw new InvalidDataException("The staged update length has changed.");
        var digest = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
        if (Volatile.Read(ref _ownership) != 0) throw new ObjectDisposedException(nameof(VerifiedUpdateDownload));
        if (!CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(Artifact.Sha256)))
            throw new InvalidDataException("The staged update no longer matches its publisher signature.");
    }
    /// <summary>The successfully launched installer now owns removal of this file.</summary>
    public void HandOffToInstaller()
    {
        if (Interlocked.CompareExchange(ref _ownership, 2, 0) != 0)
            throw new InvalidOperationException("This download has already released its ownership.");
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _ownership, 1, 0) == 0) DeleteOwnedFile(Path);
    }

    internal static void DeleteOwnedFile(string path)
    {
        try
        {
            File.Delete(path);
            var directory = System.IO.Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory)) Directory.Delete(directory); // Never recursively remove unrelated files.
        }
        catch (IOException) { /* Antivirus/readers may temporarily retain this owned file. */ }
        catch (UnauthorizedAccessException) { /* Cleanup must not hide the original download failure. */ }
    }
}
