using CAP_Core.Components.Connections;
using CAP_Core.Components.Connections.Ownership;

namespace CAP.Avalonia.ViewModels.Canvas.DieSurface;

/// <summary>Classifies wires on a die assembly for display and locking.</summary>
public static class DieWireStyle
{
    /// <summary>
    /// True when <paramref name="connection"/> is a waveguide on a single die (both ends on the
    /// same die) — die content, dimmed and locked in the System workspace. Die-to-die links are not.
    /// </summary>
    public static bool IsOnOneDie(WaveguideConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.IsCrossChipletFacetLink)
            return false;
        var die = ConnectionOwnership.DieOf(connection.StartPin);
        return die != null && ReferenceEquals(die, ConnectionOwnership.DieOf(connection.EndPin));
    }
}
