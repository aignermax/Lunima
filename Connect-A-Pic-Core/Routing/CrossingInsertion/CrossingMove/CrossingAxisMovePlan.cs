using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.CrossingInsertion.CrossingMove;

/// <summary>
/// What moving a crossing along one of its axes does to the wires docked on it, computed
/// before anything changes (see <see cref="CrossingAxisMovePlanner"/>).
/// </summary>
/// <param name="Crossing">The crossing being moved.</param>
/// <param name="Delta">The translation (µm); one component is always zero.</param>
/// <param name="Patched">
/// Pieces of the wire along the axis whose end straight only changes length: each comes with
/// its new route (an independent copy, the current one is untouched).
/// </param>
/// <param name="Rerouted">
/// Pieces that need a new route: the wire across the axis, and any piece along it whose end
/// at the crossing is no straight collinear with the axis or would become too short.
/// </param>
public sealed record CrossingAxisMovePlan(
    Component Crossing,
    (double X, double Y) Delta,
    IReadOnlyList<PatchedCrossingPiece> Patched,
    IReadOnlyList<WaveguideConnection> Rerouted);

/// <summary>A piece whose end straight follows the moved crossing, with its new route.</summary>
/// <param name="Piece">The connection docked on the crossing.</param>
/// <param name="Path">Its route after the move.</param>
public sealed record PatchedCrossingPiece(WaveguideConnection Piece, RoutedPath Path);
