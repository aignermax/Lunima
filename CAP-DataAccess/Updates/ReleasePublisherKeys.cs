using System.Collections.ObjectModel;
using System.Text.Json;

namespace CAP_DataAccess.Updates;

/// <summary>Publisher public keys reviewed and shipped with the application; never supplied by a download.</summary>
public static class ReleasePublisherKeys
{
    // The signer reads the same checked-in registry embedded here. Provision public pins
    // through review; an empty registry deliberately permits manual updates only.
    public static IReadOnlyDictionary<string, string> All { get; } = Load();

    private static IReadOnlyDictionary<string, string> Load()
    {
        using var stream = typeof(ReleasePublisherKeys).Assembly
            .GetManifestResourceStream("Lunima.Updates.PublisherKeys.json")
            ?? throw new InvalidOperationException("The publisher key registry is missing.");
        var keys = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException("The publisher key registry is invalid.");
        return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(keys, StringComparer.Ordinal));
    }
}
