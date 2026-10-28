namespace CAP_DataAccess.Import.Gds;

/// <summary>Reads GDS source bytes and parses an owned source snapshot.</summary>
public interface IGdsSourceReader
{
    Task<byte[]> ReadBytesAsync(string path, CancellationToken cancellationToken = default);
    Task<GdsLibrary> ParseAsync(byte[] source, CancellationToken cancellationToken = default);
}
