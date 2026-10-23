using CAP_Core.Export;
using CAP_Core.Export.Contracts;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>Presentation adapter over the shared bounded geometry owner.</summary>
internal sealed class GdsGeometryCache
{
    internal const int MaxEntries = PreviewGeometryCache.MaxEntries;
    private readonly IPreviewGeometryStore _owner;
    public GdsGeometryCache(IPreviewGeometryStore? owner = null) => _owner = owner ?? new PreviewGeometryCache();
    public bool TryGet(string key, out NazcaPreviewResult? value) => _owner.TryGet(key, out value);
    public void Set(string key, NazcaPreviewResult? value, long? generation = null) => _owner.Set(key, value, generation);
    public void Clear() => _owner.Clear();
    public int Count => _owner.Count;
    public long Generation => _owner.Generation;
}
