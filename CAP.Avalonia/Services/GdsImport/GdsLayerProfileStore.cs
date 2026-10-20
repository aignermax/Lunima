using System.Text.Json;
using CAP_DataAccess.Import.Gds;

namespace CAP.Avalonia.Services.GdsImport;

public sealed record GdsLayerProfile(string Name, IReadOnlyList<GdsLayerAssignment> Layers);

/// <summary>Explicitly selected lab profiles; loading a profile never confirms an import.</summary>
public sealed class GdsLayerProfileStore
{
    private readonly string _path;
    public GdsLayerProfileStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lunima", "gds-layer-profiles.json");

    public IReadOnlyList<GdsLayerProfile> Read()
    {
        if (!File.Exists(_path)) return [];
        var profiles = JsonSerializer.Deserialize<List<GdsLayerProfile>>(File.ReadAllText(_path)) ?? [];
        if (profiles.Any(x => x == null || string.IsNullOrWhiteSpace(x.Name) || x.Layers == null))
            throw new JsonException("Invalid GDS layer profile.");
        return profiles;
    }

    public bool GuideDismissed => File.Exists(_path + ".guide-seen");
    public void DismissGuide()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.WriteAllText(_path + ".guide-seen", "1");
    }

    public void Save(GdsLayerProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("A profile needs a name.");
        var profiles = Read().Where(x => !string.Equals(x.Name, profile.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        profiles.Add(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(profiles));
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
