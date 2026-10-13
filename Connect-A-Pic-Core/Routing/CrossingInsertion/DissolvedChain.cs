using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>
/// A crossing chain taken out because its wire went stale, remembered for the rest of the
/// routing pass: when the wire cannot be routed without crossings, putting the old chain
/// back and re-routing only its stale pieces is tried before a new chain is searched.
/// </summary>
/// <param name="Merged">The wire the chain merged into (routed again from scratch).</param>
/// <param name="Crossings">The crossings taken out.</param>
/// <param name="Added">The merged wires the dissolution added (the chain can only come back while all are still there).</param>
/// <param name="Undo">Puts the chain back exactly as it was.</param>
/// <param name="Redo">Takes it out again after <paramref name="Undo"/>.</param>
public sealed record DissolvedChain(
    WaveguideConnection Merged, IReadOnlyList<Component> Crossings, IReadOnlyCollection<WaveguideConnection> Added,
    Action Undo, Action Redo);
