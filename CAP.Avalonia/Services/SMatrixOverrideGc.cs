using System.Collections.Generic;
using CAP_DataAccess.Persistence.PIR;

namespace CAP.Avalonia.Services;

/// <summary>
/// Garbage-collects project-local S-matrix overrides at save time: keeps only entries
/// whose geometry key is still used by a placed component, plus legacy per-Identifier
/// entries whose component still exists, plus template-scoped ("::") entries (user-global
/// migrated). Orphans (e.g. after a parameter change) are dropped.
/// </summary>
public static class SMatrixOverrideGc
{
    /// <summary>Returns the subset of <paramref name="store"/> that should be persisted.</summary>
    public static Dictionary<string, ComponentSMatrixData> Sweep(
        IReadOnlyDictionary<string, ComponentSMatrixData> store,
        IReadOnlySet<string> usedGeometryKeys,
        IReadOnlySet<string> liveIdentifiers)
    {
        var kept = new Dictionary<string, ComponentSMatrixData>();
        foreach (var (key, value) in store)
        {
            var keep = usedGeometryKeys.Contains(key)
                       || liveIdentifiers.Contains(key)
                       || key.Contains("::", System.StringComparison.Ordinal);
            if (keep) kept[key] = value;
        }
        return kept;
    }
}
