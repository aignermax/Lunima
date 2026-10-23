using System.Text.Json;
using CAP_DataAccess.Persistence.PIR;

namespace CAP_DataAccess.Persistence.NativeDesign;

/// <summary>Persists native design documents and preserves their authorship metadata.</summary>
public static class NativeDesignFileStore
{
    /// <summary>Parses the native document without performing I/O or mutating the active design.</summary>
    public static DesignFileData? Parse(string json) => JsonSerializer.Deserialize<DesignFileData>(json);

    /// <summary>Writes the native design document with the established JSON compatibility options.</summary>
    public static async Task WriteAsync(string filePath, DesignFileData designData)
    {
        var json = JsonSerializer.Serialize(designData, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Builds the DesignMetadata for the current save, preserving the Created date from the
    /// last loaded file so that re-saving does not reset the original creation timestamp.
    /// </summary>
    public static DesignMetadata BuildMetadataForSave(DesignMetadata? loadedMetadata)
    {
        var now = DateTime.UtcNow;
        var createdDate = loadedMetadata?.Authorship?.Created
            ?? now.ToString("yyyy-MM-dd");

        return new DesignMetadata
        {
            PdkVersions = loadedMetadata?.PdkVersions ?? new Dictionary<string, string>(),
            DesignRules = loadedMetadata?.DesignRules,
            Description = loadedMetadata?.Description,
            Authorship = new AuthorshipData
            {
                Created = createdDate,
                Modified = now.ToString("o"),
                Author = loadedMetadata?.Authorship?.Author,
                Version = loadedMetadata?.Authorship?.Version
            }
        };
    }
}
