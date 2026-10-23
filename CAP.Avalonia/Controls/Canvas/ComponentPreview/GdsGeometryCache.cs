using CAP_Core.Export;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>Presentation adapter over the shared bounded geometry owner.</summary>
internal sealed class GdsGeometryCache
{
    internal const int MaxEntries = PreviewGeometryCache.MaxEntries;
    private readonly PreviewGeometryCache _owner;
    public GdsGeometryCache(PreviewGeometryCache? owner = null) => _owner = owner ?? new();
    public bool TryGet(string key, out NazcaPreviewResult? value) => _owner.TryGet(key, out value);
    public void Set(string key, NazcaPreviewResult? value, long? generation = null) => _owner.Set(key, value, generation);
    public void Clear() => _owner.Clear();
    public int Count => _owner.Count;
    public long Generation => _owner.Generation;
}
