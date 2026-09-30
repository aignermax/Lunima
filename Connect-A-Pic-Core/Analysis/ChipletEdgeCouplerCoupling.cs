using CAP_Core.Components.Connections;

namespace CAP_Core.Analysis;

/// <summary>
/// Simulation-side coupling loss for cross-chiplet edge-coupler links (issue #1228, rung 6):
/// the Gaussian mode-overlap of two identical modes with waist <see cref="ModeWaistMicrometers"/>.
/// A link qualifies exactly when <see cref="ChipletInterfaceChecker"/> would inspect it —
/// both endpoints are edge-coupler facet pins on DIFFERENT top-level chiplets — so the DRC
/// warning (#1219) and the simulated loss always agree on the same links:
/// <list type="bullet">
/// <item>facets that do not face each other (the #1219 facing check fails) couple nothing
/// (factor 0) — no invented angular model;</item>
/// <item>facing facets with lateral offset <c>d</c> couple with power
/// <c>η = exp(-d² / w0²)</c>, applied as the field factor <c>sqrt(η)</c>;</item>
/// <item>same-chiplet and non-edge-coupler links keep their transmission untouched
/// (factor 1).</item>
/// </list>
/// </summary>
public static class ChipletEdgeCouplerCoupling
{
    /// <summary>
    /// Mode-field radius w0 in micrometers — half the ~3 µm mode-field diameter of a
    /// typical SiN edge coupler (spot-size converter coupled to a lensed fiber, e.g.
    /// the Cornerstone SiN platform the rung-7 PDK targets). A named constant on
    /// purpose: #1228 deliberately does not add a PDK schema field yet.
    /// </summary>
    public const double ModeWaistMicrometers = 1.5;

    /// <summary>
    /// Power coupling η of two identical Gaussian modes whose axes are laterally offset
    /// by <paramref name="lateralOffsetMicrometers"/>: <c>exp(-d² / w0²)</c>.
    /// </summary>
    public static double PowerCouplingForOffset(double lateralOffsetMicrometers)
    {
        double ratio = lateralOffsetMicrometers / ModeWaistMicrometers;
        return Math.Exp(-ratio * ratio);
    }

    /// <summary>
    /// Field factor the link's transmission coefficient is multiplied by in the S-matrix:
    /// 1 for links that are no cross-chiplet edge-coupler connection, 0 when the facets
    /// do not face each other, <c>sqrt(η)</c> of the lateral-offset overlap otherwise.
    /// </summary>
    public static double FieldFactor(WaveguideConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!ChipletInterfaceChecker.TryGetFacet(connection.StartPin, out var start)
            || !ChipletInterfaceChecker.TryGetFacet(connection.EndPin, out var end)
            || ReferenceEquals(start.Chiplet, end.Chiplet))
        {
            return 1.0;
        }

        double deviation = ChipletInterfaceChecker.AntiparallelDeviation(
            start.Pin.GetAbsoluteAngle(), end.Pin.GetAbsoluteAngle());
        if (deviation > ChipletInterfaceChecker.FacingToleranceDegrees)
        {
            return 0.0;
        }

        var (startX, startY) = start.Pin.GetAbsolutePosition();
        var (endX, endY) = end.Pin.GetAbsolutePosition();
        double lateral = ChipletInterfaceChecker.LateralOffset(
            startX, startY, endX, endY, start.Pin.GetAbsoluteAngle());
        return Math.Sqrt(PowerCouplingForOffset(lateral));
    }
}
