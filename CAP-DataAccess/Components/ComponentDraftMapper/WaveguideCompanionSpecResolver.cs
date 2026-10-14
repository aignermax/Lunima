using CAP_Core.Components.Process;
using CAP_Core.Routing.WaveguideCompanions;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.ComponentDraftMapper
{
    /// <summary>
    /// Turns the <see cref="ProcessDefinition.WaveguideCompanionLayers"/> of a PDK process into
    /// the <see cref="WaveguideCompanionSpec"/> that canvas rendering, Nazca export and DRC
    /// share. Mirrors <see cref="WaveguideBendRadiusResolver"/>: a design-wide lookup through the
    /// active process selection and a per-connection lookup through the endpoint PDKs, so a
    /// multi-process (chiplet) canvas draws each route with its own process' bands. Malformed
    /// entries (neither or both of offset/width, negative values) are skipped, never guessed.
    /// </summary>
    public static class WaveguideCompanionSpecResolver
    {
        /// <summary>
        /// The companion spec of one process definition; <see cref="WaveguideCompanionSpec.Empty"/>
        /// when the process is null or declares no valid companion layer.
        /// </summary>
        public static WaveguideCompanionSpec FromProcess(ProcessDefinition? process)
        {
            if (process?.WaveguideCompanionLayers is not { Count: > 0 } declared)
                return WaveguideCompanionSpec.Empty;

            var layers = new List<WaveguideCompanionLayer>();
            foreach (var entry in declared)
            {
                if (TryConvert(entry) is { } layer)
                    layers.Add(layer);
            }
            return layers.Count == 0 ? WaveguideCompanionSpec.Empty : new WaveguideCompanionSpec(layers);
        }

        /// <summary>
        /// The companion spec of the design's active process: the first member PDK that declares
        /// companion layers supplies them. Playground, no selection or no drafts yield Empty.
        /// </summary>
        /// <param name="active">The design's active process selection, or null when unset.</param>
        /// <param name="loadedPdks">All currently loaded PDK drafts.</param>
        /// <param name="effectiveMemberPdkNames">Live member set replacing the persisted snapshot (null = snapshot).</param>
        public static WaveguideCompanionSpec FromActiveProcess(
            ActiveProcessSelection? active, IReadOnlyList<PdkDraft>? loadedPdks,
            IReadOnlyCollection<string>? effectiveMemberPdkNames = null)
        {
            if (active == null || active.IsPlayground || loadedPdks == null)
                return WaveguideCompanionSpec.Empty;

            var memberNames = effectiveMemberPdkNames ?? (IEnumerable<string>)active.MemberPdkNames;
            foreach (var name in memberNames)
            {
                var spec = FromProcess(FindProcess(name, loadedPdks));
                if (!spec.IsEmpty)
                    return spec;
            }
            return WaveguideCompanionSpec.Empty;
        }

        /// <summary>
        /// The companion spec of one connection from its endpoint components' PDK sources: the
        /// union of both endpoint processes' bands (a cross-chiplet route keeps both processes'
        /// layers). Returns null when neither endpoint resolves to a loaded PDK process — "no
        /// PDK opinion", the caller then uses the design-wide spec.
        /// </summary>
        public static WaveguideCompanionSpec? ResolveForEndpointPdkNames(
            string? startPdkName, string? endPdkName, IReadOnlyList<PdkDraft>? loadedPdks)
        {
            if (loadedPdks == null)
                return null;

            var startProcess = FindProcess(startPdkName, loadedPdks);
            var endProcess = FindProcess(endPdkName, loadedPdks);
            if (startProcess == null && endProcess == null)
                return null;
            if (ReferenceEquals(startProcess, endProcess))
                return FromProcess(startProcess);

            return WaveguideCompanionSpec.Union(FromProcess(startProcess), FromProcess(endProcess));
        }

        private static WaveguideCompanionLayer? TryConvert(ProcessCompanionLayer entry)
        {
            if (entry.OffsetUm.HasValue == entry.WidthUm.HasValue)
                return null;
            if (entry.OffsetUm is < 0 || entry.WidthUm is <= 0)
                return null;
            if (entry.OffsetUm is { } o && !double.IsFinite(o) || entry.WidthUm is { } w && !double.IsFinite(w))
                return null;

            return new WaveguideCompanionLayer(
                string.IsNullOrWhiteSpace(entry.Name) ? $"L{entry.Layer}/{entry.Datatype}" : entry.Name,
                entry.Layer, entry.Datatype, entry.Role, entry.OffsetUm, entry.WidthUm);
        }

        private static ProcessDefinition? FindProcess(string? pdkName, IReadOnlyList<PdkDraft> loadedPdks) =>
            string.IsNullOrEmpty(pdkName)
                ? null
                : loadedPdks.FirstOrDefault(
                    d => string.Equals(d.Name, pdkName, StringComparison.OrdinalIgnoreCase))?.Process;
    }
}
