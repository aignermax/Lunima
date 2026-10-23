using System.Text.Json;

namespace CAP_DataAccess.Persistence.NativeDesign;

/// <summary>Filesystem implementation of native document storage.</summary>
public sealed class NativeDesignFileStore : INativeDesignFileStore
{
    public bool Exists(string filePath) => File.Exists(filePath);
    public Task<string> ReadAsync(string filePath, CancellationToken cancellationToken = default) =>
        File.ReadAllTextAsync(filePath, cancellationToken);

    public async Task WriteAsync(string filePath, DesignFileData data, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }
}
