using CAP_Core.Export;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>
/// Best-effort disk cache. Each render key owns one file; environment changes overwrite
/// that file instead of accumulating namespaces. Old untagged entries are ignored by
/// environment-aware consumers. Direct callers retain the original untagged API.
/// </summary>
public sealed class GdsPreviewDiskCache : IGdsPreviewCache
{
    private const string EmptyMarker = "__EMPTY__";
    private readonly string _directory;
    private readonly object _gate = new();
    private string? _activation;

    public GdsPreviewDiskCache() : this(Path.Combine(
        CAP_Core.Helpers.AppDataFolders.LocalApplicationData, "Lunima", "gds-preview-cache")) { }
    public GdsPreviewDiskCache(string directory) => _directory = directory;
    private string PathFor(GdsPreviewKey key) => Path.Combine(_directory, key.Hash() + ".json");

    /// <summary>Persists activation of even the same interpreter path across restarts.</summary>
    public string ActivationIdentity
    {
        get
        {
            lock (_gate)
            {
                if (_activation != null) return _activation;
                try { _activation = File.ReadAllText(Path.Combine(_directory, "activation")); }
                catch { _activation = "initial"; }
                return _activation;
            }
        }
    }

    public void InvalidateEnvironment()
    {
        lock (_gate)
        {
            _activation = Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(_directory);
                File.WriteAllText(Path.Combine(_directory, "activation"), _activation);
            }
            catch { /* In-memory invalidation still applies when the cache directory is unavailable. */ }
        }
    }

    public bool TryRead(GdsPreviewKey key, out NazcaPreviewResult? result, string? environment = null)
    {
        result = null;
        try
        {
            var path = PathFor(key);
            if (!File.Exists(path)) return false;
            var json = File.ReadAllText(path);
            if (environment != null)
            {
                var prefix = "environment:" + environment + "\n";
                if (!json.StartsWith(prefix, StringComparison.Ordinal)) return false;
                json = json[prefix.Length..];
            }
            if (json == EmptyMarker) return true;
            result = NazcaPreviewResultDto.Deserialize(json);
            return result != null;
        }
        catch { return false; }
    }

    public void Write(GdsPreviewKey key, NazcaPreviewResult result, string? environment = null)
    {
        try { WritePayload(key, NazcaPreviewResultDto.Serialize(result), environment); }
        catch { /* Serialization is also best-effort. */ }
    }

    public void WriteEmpty(GdsPreviewKey key, string? environment = null) => WritePayload(key, EmptyMarker, environment);

    private void WritePayload(GdsPreviewKey key, string json, string? environment)
    {
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(_directory);
            var path = PathFor(key);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, environment == null ? json : "environment:" + environment + "\n" + json);
            File.Move(temporary, path, overwrite: true);
        }
        catch { /* best-effort */ }
        finally
        {
            try { if (temporary != null) File.Delete(temporary); }
            catch { /* best-effort temporary cleanup */ }
        }
    }
}
