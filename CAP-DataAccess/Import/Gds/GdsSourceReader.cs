namespace CAP_DataAccess.Import.Gds;

/// <summary>File access and isolated per-call GDS parsing, safe for concurrent imports.</summary>
public sealed class GdsSourceReader : IGdsSourceReader
{
    public async Task<byte[]> ReadBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"GDS file not found: {path}", path);
        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GdsLibrary> ParseAsync(byte[] source, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(source, writable: false);
        // GdsReader holds mutable record state: never share it across calls.
        return await new GdsReader().ReadAsync(stream, cancellationToken).ConfigureAwait(false);
    }
}
