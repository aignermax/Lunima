using System.Text.Json;

namespace CAP_DataAccess.Persistence.NativeDesign;

/// <summary>Filesystem implementation of native document storage.</summary>
public sealed class NativeDesignFileStore : INativeDesignFileStore
{
    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public bool Exists(string filePath) => File.Exists(filePath);
    public Task<string> ReadAsync(string filePath, CancellationToken cancellationToken = default) =>
        File.ReadAllTextAsync(filePath, cancellationToken);

    /// <summary>Serializes an exclusively owned capture off the UI thread and atomically publishes it.</summary>
    public Task WriteAsync(string filePath, DesignFileData data, CancellationToken cancellationToken = default) =>
        Task.Run(() => AtomicFileWriter.WriteAsync(filePath,
            (stream, token) => JsonSerializer.SerializeAsync(stream, data, SaveOptions, token), cancellationToken));
}
