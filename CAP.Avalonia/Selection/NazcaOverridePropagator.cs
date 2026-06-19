using CAP_DataAccess.Persistence.PIR;

namespace CAP.Avalonia.Selection;

/// <summary>
/// Copies per-instance Nazca code overrides onto pasted component copies.
/// Overrides are keyed by component identifier; a freshly pasted clone gets a new
/// identifier and would otherwise lose its override (and thus its raw-code preview
/// and export geometry). This propagator duplicates each source override under the
/// copy's identifier so the copy behaves like the original.
/// </summary>
public static class NazcaOverridePropagator
{
    /// <summary>
    /// For every (oldId → newId) pair in <paramref name="identifierMap"/> that has an
    /// override under <c>oldId</c>, writes an independent <see cref="NazcaCodeOverride.Clone"/>
    /// under <c>newId</c>. Existing entries for <c>newId</c> are left untouched, and
    /// identity mappings (oldId == newId) are skipped.
    /// </summary>
    /// <param name="identifierMap">Old-to-new component identifier mapping from a paste.</param>
    /// <param name="overrides">The live override store to extend (e.g. <c>StoredNazcaOverrides</c>).</param>
    /// <returns>The number of overrides propagated onto copies.</returns>
    public static int Propagate(
        IReadOnlyDictionary<string, string> identifierMap,
        IDictionary<string, NazcaCodeOverride> overrides)
    {
        if (identifierMap == null || overrides == null) return 0;

        var propagated = 0;
        foreach (var (oldId, newId) in identifierMap)
        {
            if (oldId == newId) continue;
            if (overrides.ContainsKey(newId)) continue;
            if (!overrides.TryGetValue(oldId, out var source)) continue;

            overrides[newId] = source.Clone();
            propagated++;
        }

        return propagated;
    }
}
