namespace CAP_DataAccess.Import;

/// <summary>
/// Helpers for reconciling imported S-parameter port names (which often use
/// generic labels like "port 1", "port 2", …) with the semantic pin names
/// of a Lunima component (e.g. "in", "out1", "out2"). The applicator refuses
/// to silently match across naming schemes — see
/// <c>SMatrixOverrideApplicator.ResolvePins</c> — so the import flow has to
/// resolve the names up front, either automatically when they happen to
/// match or via a user dialog.
/// </summary>
public static class PortNameMapping
{
    /// <summary>
    /// Returns true when every imported port name appears verbatim in
    /// <paramref name="componentPinNames"/> (case-insensitive). When this
    /// holds, the import data can flow straight into the override store —
    /// no user mapping required.
    /// </summary>
    public static bool NamesAlignWithComponent(
        IEnumerable<string> importedNames,
        IEnumerable<string> componentPinNames)
    {
        var componentSet = new HashSet<string>(componentPinNames, StringComparer.OrdinalIgnoreCase);
        return importedNames.All(componentSet.Contains);
    }

    /// <summary>
    /// Builds a default mapping <c>imported name → component pin name</c> by
    /// (a) keeping a name unchanged if the component already has a pin with
    /// the same name, otherwise (b) falling back to positional pairing
    /// (imported[i] → componentPinNames[i]). The user can override any row
    /// in the mapping dialog before the result is applied.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the two lists have different lengths — a positional
    /// fallback would be ambiguous and we'd rather fail loud than silently
    /// drop or fabricate ports.
    /// </exception>
    public static Dictionary<string, string> BuildDefaultMapping(
        IReadOnlyList<string> importedNames,
        IReadOnlyList<string> componentPinNames)
    {
        if (importedNames.Count != componentPinNames.Count)
            throw new ArgumentException(
                $"Imported port count ({importedNames.Count}) must equal component pin count " +
                $"({componentPinNames.Count}) before a positional fallback can be derived.");

        var componentSet = new HashSet<string>(componentPinNames, StringComparer.OrdinalIgnoreCase);
        var mapping = new Dictionary<string, string>(importedNames.Count, StringComparer.Ordinal);

        for (int i = 0; i < importedNames.Count; i++)
        {
            var imported = importedNames[i];
            mapping[imported] = componentSet.Contains(imported)
                ? imported // already aligned — keep it
                : componentPinNames[i]; // positional fallback
        }

        return mapping;
    }

    /// <summary>
    /// Returns a copy of <paramref name="imported"/> with port names rewritten
    /// according to <paramref name="mapping"/>. The S-matrix data and indexing
    /// stay untouched — relabelling alone is enough because the applicator
    /// keys on names, not on the original Lumerical labels.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when an imported name has no entry in <paramref name="mapping"/>
    /// — better to fail than silently drop one port and shift the rest.
    /// </exception>
    public static ImportedSParameters Remap(
        ImportedSParameters imported,
        IReadOnlyDictionary<string, string> mapping)
    {
        var renamed = new List<string>(imported.PortNames.Count);
        foreach (var oldName in imported.PortNames)
        {
            if (!mapping.TryGetValue(oldName, out var newName))
                throw new ArgumentException(
                    $"Mapping does not cover imported port '{oldName}'. " +
                    $"Provide a target for every imported name (got " +
                    $"{string.Join(", ", mapping.Keys)}).");
            renamed.Add(newName);
        }

        // SMatricesByWavelengthNm and PortCount are unchanged: we only
        // rewrote the labels, not the matrix layout.
        return new ImportedSParameters
        {
            SourceFormat = imported.SourceFormat,
            SourceFilePath = imported.SourceFilePath,
            PortCount = imported.PortCount,
            PortNames = renamed,
            SMatricesByWavelengthNm = imported.SMatricesByWavelengthNm,
            Metadata = imported.Metadata,
        };
    }
}
