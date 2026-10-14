using CAP_Core.Components.Core;
using CAP_Core.LightCalculation.BendLoss;

namespace CAP_Core.Components.Connections;

/// <summary>
/// Bend-loss model resolution for new connections: a connection inherits the radius-dependent
/// model of its endpoint components' PDK process, so its transmission depends on the actual
/// platform and bend radii.
/// </summary>
public partial class WaveguideConnectionManager
{
    private const double QuarterTurnDegrees = 90.0;

    /// <summary>
    /// Bend-loss model for new optical connections whose endpoint components carry no PDK
    /// model (<see cref="Component.WaveguideBendLoss"/>). Default: the generic preset.
    /// </summary>
    public BendLossModel DefaultBendLossModel { get; set; } = BendLossPresets.Generic;

    /// <summary>
    /// Resolves the bend-loss model a new connection inherits. Metal traces radiate no light
    /// (<see cref="BendLossPresets.None"/>). Optical connections take the lossier of the two
    /// endpoint PDK models (compared at the default radius) — a route touching a SiN chiplet
    /// bends in nitride — else <see cref="DefaultBendLossModel"/>.
    /// </summary>
    private BendLossModel ResolveBendLoss(PhysicalPin startPin, PhysicalPin endPin)
    {
        bool isElectrical = startPin?.MatterType == MatterType.Electricity ||
                            endPin?.MatterType == MatterType.Electricity;
        if (isElectrical)
            return BendLossPresets.None;

        var start = startPin?.ParentComponent?.WaveguideBendLoss;
        var end = endPin?.ParentComponent?.WaveguideBendLoss;
        if (start == null || end == null)
            return start ?? end ?? DefaultBendLossModel;

        double probeRadius = WaveguideConnection.DefaultBendRadiusMicrometers;
        return end.CornerLossDb(probeRadius, QuarterTurnDegrees) > start.CornerLossDb(probeRadius, QuarterTurnDegrees)
            ? end
            : start;
    }
}
