using CAP_Core.Components.Core;

namespace CAP_Core.Export;

/// <summary>
/// Where and how a component's Nazca cell is placed in the exported script.
/// The anchor is always the cell's 'org' pin:
/// <c>factory().put('org', X, Y, RotationDegrees)</c>.
/// </summary>
/// <param name="X">Nazca X coordinate where the cell origin is put.</param>
/// <param name="Y">Nazca Y coordinate where the cell origin is put.</param>
/// <param name="RotationDegrees">Put rotation, i.e. the negated app rotation (Nazca is Y-up).</param>
public record CellPlacement(double X, double Y, double RotationDegrees);

/// <summary>
/// Single source of truth for all app→Nazca coordinate answers (issue #565).
/// App space is Y-down with the component box anchored at its top-left corner;
/// Nazca space is Y-up. Points convert by Y negation, angles by negation.
/// </summary>
public static class NazcaCoordinateMapper
{
    /// <summary>
    /// Computes where the component's cell must be put ('org'-anchored) so that the
    /// rotated cell geometry lands exactly on the component's app-space box.
    /// The cell-internal unrotated bbox is rotated by the put rotation and re-anchored
    /// so its top-left corner hits the app box top-left (PhysicalX, -PhysicalY) — the
    /// app rotates pin offsets about the box centre while keeping the top-left fixed,
    /// and rotating about a different point only differs by a translation that this
    /// bbox re-anchoring absorbs exactly.
    /// </summary>
    /// <param name="comp">The component to place.</param>
    /// <param name="rawOverrideAnchor">
    /// Cell-internal bbox anchor (XMin, YMax) persisted with a raw-code override,
    /// or null for PDK/legacy components.
    /// </param>
    public static CellPlacement GetCellPlacement(
        Component comp, (double XMin, double YMax)? rawOverrideAnchor)
    {
        var (w0, h0) = GetUnrotatedDimensions(comp);
        var bbox = GetUnrotatedCellBbox(comp, rawOverrideAnchor, w0, h0);
        double putRotation = NormalizeZero(-comp.RotationDegrees);
        var (minX, maxY) = GetRotatedBboxAnchor(bbox, putRotation);
        return new CellPlacement(
            NormalizeZero(comp.PhysicalX - minX),
            NormalizeZero(-comp.PhysicalY - maxY),
            putRotation);
    }

    /// <summary>
    /// World-space Nazca position of a pin. Universal plain Y negation of the app
    /// position: the app model is the truth for where pins ARE (the canvas draws them
    /// there); calibration data only influences where the CELL is placed so its
    /// rendered pins coincide with the app pins.
    /// </summary>
    public static (double X, double Y) GetPinNazcaPosition(PhysicalPin pin)
    {
        var (x, y) = pin.GetAbsolutePosition();
        return ToNazca(x, y);
    }

    /// <summary>
    /// World-space Nazca angle of a pin: the negated app world angle
    /// (AngleDegrees + RotationDegrees, normalised to [0, 360) first), so the result
    /// lies in (-360, 0] — the convention the exporter has always emitted.
    /// </summary>
    public static double GetPinNazcaAngle(PhysicalPin pin) =>
        NormalizeZero(-pin.GetAbsoluteAngle());

    /// <summary>
    /// Converts an app-space point (Y-down) to Nazca space (Y-up).
    /// </summary>
    public static (double X, double Y) ToNazca(double appX, double appY) =>
        (appX, NormalizeZero(-appY));

    /// <summary>
    /// Returns true if the function name looks like a real PDK function (e.g.
    /// "ebeam_y_1550"). Recognizes SiEPIC EBeam PDK naming patterns and module
    /// dot-notation; demo_pdk stubs are excluded because they are exported as
    /// locally generated cells, not PDK calls.
    /// </summary>
    public static bool IsPdkFunction(string name) =>
        name.StartsWith("ebeam_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("GC_", StringComparison.Ordinal) ||
        name.StartsWith("ANT_", StringComparison.Ordinal) ||
        name.StartsWith("crossing_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("taper_", StringComparison.OrdinalIgnoreCase) ||
        // SiEPIC ships a few generic-named PCells too (the `contra_*` family
        // is the directional-coupler set under the EBeam library).
        name.StartsWith("contra_", StringComparison.OrdinalIgnoreCase) ||
        (name.Contains('.', StringComparison.Ordinal) &&
         !name.StartsWith("demo_pdk.", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Checks if a component is a parametric straight waveguide: the function name
    /// hints at a straight ("straight"/"strt") and the parameters carry a length
    /// argument, so the export emits nd.strt(length=...) instead of a fixed cell.
    /// </summary>
    public static bool IsParametricStraight(string funcName, string parameters)
    {
        if (string.IsNullOrEmpty(parameters))
            return false;

        var lower = funcName.ToLowerInvariant();
        var hasLength = parameters.Contains("length=", StringComparison.OrdinalIgnoreCase);
        var isStraight = lower.Contains("straight") || lower.Contains("strt");

        return hasLength && isStraight;
    }

    /// <summary>
    /// Unrotated cell dimensions. Component.Width/Height hold the CURRENT
    /// (rotation-swapped) values, so 90°/270° states swap them back.
    /// </summary>
    private static (double Width, double Height) GetUnrotatedDimensions(Component comp) =>
        comp.RotationDegrees % 180 == 0
            ? (comp.WidthMicrometers, comp.HeightMicrometers)
            : (comp.HeightMicrometers, comp.WidthMicrometers);

    /// <summary>
    /// Cell-internal unrotated bbox (Nazca Y-up, org = cell origin) — the single
    /// branching point between the component kinds.
    /// </summary>
    private static (double XMin, double YMin, double XMax, double YMax) GetUnrotatedCellBbox(
        Component comp, (double XMin, double YMax)? rawOverrideAnchor, double w0, double h0)
    {
        // A raw-code override persists its own measured bbox anchor; the cell geometry
        // is whatever the user's code renders, so the anchor is authoritative.
        if (rawOverrideAnchor is { } anchor)
            return (anchor.XMin, anchor.YMax - h0, anchor.XMin + w0, anchor.YMax);

        // Calibrated/heuristic origin offset (ox, oy): at rotation 0 the org pin must
        // land on (PhysicalX + ox, -(PhysicalY + oy)) — the long-standing calibrated
        // convention — which parameterises the bbox as [-ox, oy - H0, -ox + W0, oy].
        var (ox, oy) = GetOriginOffset(comp, h0);
        return (-ox, oy - h0, -ox + w0, oy);
    }

    /// <summary>
    /// Origin offset (ox, oy) of a non-override component. Detection rules are
    /// the established export heuristics:
    /// PDK name or explicit calibrated offset → stored NazcaOriginOffset (issue #355
    /// covers auto-generated names with a calibrated offset); parametric straights
    /// anchor on their first pin; everything else uses the legacy bottom-left fallback.
    /// </summary>
    private static (double OffsetX, double OffsetY) GetOriginOffset(
        Component comp, double unrotatedHeight)
    {
        var funcName = comp.NazcaFunctionName;

        bool hasPdkFunctionName = !string.IsNullOrEmpty(funcName) &&
            (IsPdkFunction(funcName) || funcName.StartsWith("demo_pdk.", StringComparison.OrdinalIgnoreCase));
        bool hasExplicitOriginOffset = comp.NazcaOriginOffsetX != 0 || comp.NazcaOriginOffsetY != 0;

        if (hasPdkFunctionName || hasExplicitOriginOffset)
            return (comp.NazcaOriginOffsetX, comp.NazcaOriginOffsetY);

        if (IsParametricStraight(funcName, comp.NazcaFunctionParameters))
        {
            var firstPin = comp.PhysicalPins.FirstOrDefault();
            if (firstPin != null)
                return (firstPin.OffsetXMicrometers, firstPin.OffsetYMicrometers);
        }

        // Legacy components without calibration data: org at the box bottom-left.
        return (0, unrotatedHeight);
    }

    /// <summary>
    /// Rotates the bbox corners by the put rotation and returns the values the
    /// placement needs: the rotated bbox's min X and max Y (its top-left corner).
    /// </summary>
    private static (double MinX, double MaxY) GetRotatedBboxAnchor(
        (double XMin, double YMin, double XMax, double YMax) bbox, double rotationDegrees)
    {
        double rad = rotationDegrees * Math.PI / 180.0;
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);

        double minX = double.PositiveInfinity;
        double maxY = double.NegativeInfinity;
        foreach (var (x, y) in new[]
        {
            (bbox.XMin, bbox.YMin), (bbox.XMax, bbox.YMin),
            (bbox.XMax, bbox.YMax), (bbox.XMin, bbox.YMax),
        })
        {
            minX = Math.Min(minX, x * cos - y * sin);
            maxY = Math.Max(maxY, x * sin + y * cos);
        }
        return (minX, maxY);
    }

    /// <summary>
    /// Normalizes negative zero to positive zero so formatted output never
    /// shows "-0.00".
    /// </summary>
    private static double NormalizeZero(double value) =>
        value == 0.0 ? 0.0 : value;
}
