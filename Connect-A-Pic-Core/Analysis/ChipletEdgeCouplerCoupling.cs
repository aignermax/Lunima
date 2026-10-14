using CAP_Core.Components.Connections;

namespace CAP_Core.Analysis;

/// <summary>
/// Simulation-side coupling loss for cross-chiplet edge-coupler links (issues #1228/#1238, rung 6):
/// the Gaussian mode-overlap of two identical modes with waist <see cref="ModeWaistMicrometers"/>.
/// A link qualifies exactly when <see cref="ChipletInterfaceChecker"/> would inspect it —
/// both endpoints are edge-coupler facet pins on DIFFERENT top-level chiplets — so the DRC
/// warning (#1219) and the simulated loss always agree on the same links:
/// <list type="bullet">
/// <item>facets that do not face each other (the #1219 facing check fails) couple nothing
/// (factor 0) — no invented angular model;</item>
/// <item>facing facets with lateral offset <c>d</c> couple with power
/// <c>η = exp(-d² / w0²)</c>;</item>
/// <item>facing facets separated by a longitudinal gap <c>z</c> additionally lose to
/// Gaussian beam divergence: <c>η_gap = 1 / (1 + (z / (2·z_R))²)</c> with Rayleigh
/// range <c>z_R = π·n·w0² / λ</c> (issue #1238);</item>
/// <item>total power coupling is <c>η_offset · η_gap</c>, applied as the field factor
/// <c>sqrt(η_total)</c>;</item>
/// <item>couplers with different mode sizes (PDK field <c>modeFieldDiameterMicrometers</c>,
/// waists w1, w2) additionally lose to spot-size mismatch: on axis
/// <c>η = 4·w1²·w2² / ((w1² + w2²)² + (λ·z / (π·n))²)</c>, which reduces to the identical-mode
/// gap formula above for w1 = w2, and the offset term becomes <c>exp(-2·d² / (w1² + w2²))</c>
/// (exact at zero gap);</item>
/// <item>the medium filling the gap is air unless the link carries its own refractive
/// index (<see cref="WaveguideConnection.FacetGapMediumIndex"/>, e.g. index-matching
/// adhesive);</item>
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
    /// Refractive index of the medium filling the facet gap: air. A named constant on
    /// purpose — underfill or index-matching adhesive would need a PDK/schema field,
    /// which this rung deliberately does not add.
    /// </summary>
    public const double GapMediumRefractiveIndex = 1.0;

    private const double NanometersPerMicrometer = 1000.0;
    private const double DiameterPerRadius = 2.0;

    /// <summary>
    /// Mode-field radius (waist) of an edge coupler: half its PDK-declared mode-field
    /// diameter, or <see cref="ModeWaistMicrometers"/> when the part declares none.
    /// </summary>
    public static double WaistOf(CAP_Core.Components.Core.Component? coupler) =>
        coupler?.ModeFieldDiameterMicrometers is > 0 and var diameter
            ? diameter / DiameterPerRadius
            : ModeWaistMicrometers;

    /// <summary>
    /// Power coupling of two Gaussian modes with waists <paramref name="waistAMicrometers"/>
    /// and <paramref name="waistBMicrometers"/> facing each other across a gap of
    /// <paramref name="gapMicrometers"/> in a medium of index <paramref name="mediumIndex"/>,
    /// laterally offset by <paramref name="lateralOffsetMicrometers"/>: the exact on-axis
    /// mismatch-and-divergence overlap times the offset overlap.
    /// </summary>
    public static double PowerCoupling(
        double waistAMicrometers, double waistBMicrometers,
        double lateralOffsetMicrometers, double gapMicrometers,
        double wavelengthNm, double mediumIndex = GapMediumRefractiveIndex)
    {
        double w1Sq = waistAMicrometers * waistAMicrometers;
        double w2Sq = waistBMicrometers * waistBMicrometers;
        double sum = w1Sq + w2Sq;
        double wavelengthMicrometers = wavelengthNm / NanometersPerMicrometer;
        double divergence = wavelengthMicrometers * Math.Max(0.0, gapMicrometers) / (Math.PI * mediumIndex);
        double onAxis = 4.0 * w1Sq * w2Sq / (sum * sum + divergence * divergence);
        double offset = Math.Exp(-2.0 * lateralOffsetMicrometers * lateralOffsetMicrometers / sum);
        return onAxis * offset;
    }

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
    /// Power coupling η_gap of two identical Gaussian modes facing each other across a
    /// free-space gap of <paramref name="gapMicrometers"/>:
    /// <c>1 / (1 + (z / (2·z_R))²)</c> with Rayleigh range
    /// <c>z_R = π·n·w0² / λ</c> at the simulation wavelength
    /// <paramref name="wavelengthNm"/> (no hidden default — the caller must know which
    /// wavelength the S-matrix is built for). A negative or overlapping gap counts as 0:
    /// the overlap is a separate DRC concern, not a coupling gain.
    /// </summary>
    public static double PowerCouplingForGap(double gapMicrometers, double wavelengthNm)
    {
        double wavelengthMicrometers = wavelengthNm / NanometersPerMicrometer;
        double rayleighRange = Math.PI * GapMediumRefractiveIndex
            * ModeWaistMicrometers * ModeWaistMicrometers / wavelengthMicrometers;
        double ratio = Math.Max(0.0, gapMicrometers) / (2.0 * rayleighRange);
        return 1.0 / (1.0 + ratio * ratio);
    }

    /// <summary>
    /// Field factor the link's transmission coefficient is multiplied by in the S-matrix:
    /// 1 for links that are no cross-chiplet edge-coupler connection, 0 when the facets
    /// do not face each other, <c>sqrt(η_offset · η_gap)</c> of the lateral-offset and
    /// longitudinal-gap overlap otherwise, evaluated at
    /// <paramref name="wavelengthNm"/> — the same wavelength the S-matrix is built for.
    /// </summary>
    public static double FieldFactor(WaveguideConnection connection, double wavelengthNm)
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
        double startAngle = start.Pin.GetAbsoluteAngle();
        double lateral = ChipletInterfaceChecker.LateralOffset(startX, startY, endX, endY, startAngle);
        double gap = ChipletInterfaceChecker.AxialGap(startX, startY, endX, endY, startAngle);
        return Math.Sqrt(PowerCoupling(
            WaistOf(start.Pin.ParentComponent), WaistOf(end.Pin.ParentComponent),
            lateral, gap, wavelengthNm, connection.FacetGapMediumIndex));
    }
}
