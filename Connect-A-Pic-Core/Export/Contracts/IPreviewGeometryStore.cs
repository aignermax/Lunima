namespace CAP_Core.Export.Contracts;

/// <summary>Bounded geometry retention with generation-checked publication after invalidation.</summary>
public interface IPreviewGeometryStore
{
    bool TryGet(string key, out NazcaPreviewResult? result);
    void Set(string key, NazcaPreviewResult? result, long? expectedGeneration = null);
    void Clear();
    int Count { get; }
    long Generation { get; }
}
