using System.Text.Json;
using CAP_DataAccess.Persistence.PIR;

namespace CAP_DataAccess.Persistence.NativeDesign;

/// <summary>Native format interpretation and authorship policy, independent of file I/O.</summary>
public static class NativeDesignCodec
{
    /// <summary>Parses the native document without performing I/O or mutating the active design.</summary>
    public static DesignFileData? Parse(string json) => JsonSerializer.Deserialize<DesignFileData>(json);

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
            PdkVersions = loadedMetadata?.PdkVersions is { } versions
                ? new Dictionary<string, string>(versions, versions.Comparer) : new(),
            DesignRules = loadedMetadata?.DesignRules?.Copy(),
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
