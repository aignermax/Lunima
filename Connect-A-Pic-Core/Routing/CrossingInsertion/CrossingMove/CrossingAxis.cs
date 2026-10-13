namespace CAP_Core.Routing.CrossingInsertion.CrossingMove;

/// <summary>
/// The axis a crossing is moved along — the direction of one of the two wires through it.
/// Moving along an axis slides the crossing along the wire running in that direction; the
/// wire running across it has to be re-routed.
/// </summary>
public enum CrossingAxis
{
    /// <summary>Along X: follows the wire through the west/east ports.</summary>
    Horizontal,

    /// <summary>Along Y: follows the wire through the north/south ports.</summary>
    Vertical,
}
