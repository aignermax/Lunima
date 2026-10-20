using CAP_Core.Components.Core;
using CAP_Core.Components.PinKinds;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Pure compatibility check used while dragging a connection: decides whether a hovered
/// candidate pin should render dimmed as an invalid target (issue #724 point 4). Before this
/// fix, <see cref="PinRenderer"/> only dimmed on a <see cref="PolarizationRules"/> mismatch, so
/// dragging from an optical pin over an incompatible electrical pin (or vice versa) showed no
/// visual affordance until the user released and got a rejection message from
/// <c>ConnectionGestureRecognizer</c>.
/// </summary>
internal static class PinConnectionAffordance
{
    /// <summary>
    /// True when <paramref name="candidate"/> cannot be connected to <paramref name="dragStart"/>:
    /// a vertical fiber terminal, a different signal domain (optical vs. electrical — see
    /// <see cref="PinKindHelper.ArePlanarRoutingCompatible"/>) or an incompatible polarization (TE vs.
    /// TM — see <see cref="PolarizationRules.CanConnect"/>). The preview uses the same planar
    /// compatibility policy as connection creation.
    /// </summary>
    public static bool IsIncompatibleTarget(PhysicalPin dragStart, PhysicalPin candidate)
        => !PinKindHelper.ArePlanarRoutingCompatible(dragStart, candidate)
           || !PolarizationRules.CanConnect(dragStart.Polarization, candidate.Polarization);
}
