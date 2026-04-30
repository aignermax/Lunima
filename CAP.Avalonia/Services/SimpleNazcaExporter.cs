using System.Globalization;
using System.Text;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;

namespace CAP.Avalonia.Services;

/// <summary>
/// Simple Nazca exporter for the physical coordinate system.
/// Exports components and waveguide connections to Python/Nazca code.
/// </summary>
public class SimpleNazcaExporter
{
    /// <summary>
    /// Exports the full design to a Python/Nazca script.
    /// </summary>
    /// <param name="canvas">The design canvas to export.</param>
    /// <param name="pdkModuleName">Optional PDK module name (e.g., "siepic_ebeam_pdk") for import.</param>
    public string Export(DesignCanvasViewModel canvas, string? pdkModuleName = null)
    {
        var sb = new StringBuilder();

        AppendHeader(sb);
        AppendPdkComponentStubs(sb, canvas);
        var componentNames = AppendComponents(sb, canvas);
        AppendConnections(sb, canvas, componentNames);
        AppendFooter(sb);

        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb)
    {
        sb.AppendLine("import nazca as nd");
        sb.AppendLine("import nazca.demofab as demo");
        sb.AppendLine("from nazca.interconnects import Interconnect");
        sb.AppendLine();
        sb.AppendLine("# PDK Configuration");
        sb.AppendLine("WG_WIDTH = 0.45  # Waveguide width in µm");
        sb.AppendLine("BEND_RADIUS = 50  # Minimum bend radius in µm");
        sb.AppendLine();
        sb.AppendLine("# Create interconnect for waveguide routing");
        sb.AppendLine("ic = Interconnect(width=WG_WIDTH, radius=BEND_RADIUS)");
        sb.AppendLine();
    }

    /// <summary>
    /// Generates standalone Nazca cell definitions for PDK components.
    /// Each unique PDK function used in the design gets a stub cell
    /// with correct dimensions and pin positions — no external PDK install needed.
    /// ComponentGroups are flattened — stubs are generated for all child components.
    /// </summary>
    private static void AppendPdkComponentStubs(StringBuilder sb, DesignCanvasViewModel canvas)
    {
        var ci = CultureInfo.InvariantCulture;
        var generated = new HashSet<string>(StringComparer.Ordinal);

        foreach (var compVm in canvas.Components)
        {
            var comp = compVm.Component;
            if (comp is ComponentGroup group)
            {
                foreach (var child in group.GetAllComponentsRecursive())
                    AppendComponentStub(sb, child, generated, ci);
            }
            else
            {
                AppendComponentStub(sb, comp, generated, ci);
            }
        }
    }

    /// <summary>
    /// Generates a PDK stub for a single component if required.
    /// </summary>
    private static void AppendComponentStub(
        StringBuilder sb, Component comp, HashSet<string> generated, CultureInfo ci)
    {
        var funcName = comp.NazcaFunctionName;
        if (string.IsNullOrEmpty(funcName) || !RequiresStub(funcName))
            return;
        if (!generated.Add(funcName))
            return;

        if (IsParametricStraight(funcName, comp.NazcaFunctionParameters))
            AppendParametricStraightStub(sb, funcName, comp, ci);
        else
            AppendStandardComponentStub(sb, funcName, comp, ci);
    }

    /// <summary>
    /// Checks if a function requires a stub definition.
    /// Returns true for real PDK functions and demo_pdk functions.
    /// </summary>
    private static bool RequiresStub(string funcName) =>
        IsPdkFunction(funcName) ||
        funcName.StartsWith("demo_pdk.", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Checks if a component is a parametric straight waveguide.
    /// </summary>
    private static bool IsParametricStraight(string funcName, string parameters)
    {
        if (string.IsNullOrEmpty(parameters))
            return false;

        var lower = funcName.ToLowerInvariant();
        var hasLength = parameters.Contains("length=", StringComparison.OrdinalIgnoreCase);
        var isStraight = lower.Contains("straight") || lower.Contains("strt");

        return hasLength && isStraight;
    }

    /// <summary>
    /// Generates a parametric straight waveguide stub that uses nd.strt() with length parameter.
    /// </summary>
    private static void AppendParametricStraightStub(
        StringBuilder sb, string funcName, Component comp, CultureInfo ci)
    {
        var h = comp.HeightMicrometers.ToString("F2", ci);

        // Sanitize function name for valid Python identifier (replace non-alphanumeric/underscore chars)
        var pythonFuncName = System.Text.RegularExpressions.Regex.Replace(funcName, @"[^a-zA-Z0-9_]", "_");

        sb.AppendLine($"def {pythonFuncName}(length=100, **kwargs):");
        sb.AppendLine($"    \"\"\"Auto-generated parametric straight waveguide stub for {funcName}.\"\"\"");
        sb.AppendLine($"    with nd.Cell(name='{funcName}_{{length}}') as cell:");
        sb.AppendLine($"        # Use nd.strt() for proper waveguide with specified length");
        sb.AppendLine($"        nd.strt(length=length, width=0.45, layer=1).put(0, {h}/2)");

        // Generate pins with Nazca coordinates
        foreach (var pin in comp.PhysicalPins)
        {
            var py = NormalizeZero(comp.HeightMicrometers - pin.OffsetYMicrometers).ToString("F2", ci);
            var pa = NormalizeZero(-pin.AngleDegrees).ToString("F0", ci);

            // For straight waveguides: input pin at x=0, output pin at x=length
            if (pin.OffsetXMicrometers == 0)
            {
                sb.AppendLine($"        nd.Pin('{pin.Name}').put(0, {py}, {pa})");
            }
            else
            {
                sb.AppendLine($"        nd.Pin('{pin.Name}').put(length, {py}, {pa})");
            }
        }

        sb.AppendLine($"    return cell");
        sb.AppendLine();
    }

    /// <summary>
    /// Generates a standard non-parametric component stub using a polygon box.
    /// When NazcaOriginOffset is set, the polygon box is shifted so that the cell's
    /// (0,0) origin is at the offset position, not the bottom-left corner.
    /// This ensures component boxes appear in the correct location in KLayout/GDS viewers.
    /// </summary>
    private static void AppendStandardComponentStub(
        StringBuilder sb, string funcName, Component comp, CultureInfo ci)
    {
        var w = comp.WidthMicrometers;
        var h = comp.HeightMicrometers;

        // Sanitize function name for valid Python identifier (replace non-alphanumeric/underscore chars)
        var pythonFuncName = System.Text.RegularExpressions.Regex.Replace(funcName, @"[^a-zA-Z0-9_]", "_");

        // Define cell once, return cached instance on each call
        sb.AppendLine($"with nd.Cell(name='{funcName}') as _{pythonFuncName}_cell:");
        sb.AppendLine($"    \"\"\"Auto-generated stub for {funcName} ({comp.WidthMicrometers}x{comp.HeightMicrometers} µm).\"\"\"");

        // When NazcaOriginOffset is set, the cell origin (0,0) is NOT at the bottom-left corner.
        // The polygon must be shifted so it appears at the correct position relative to the origin.
        // Example: GC with offset (15, 30) → polygon from (-15, -30) to (15, 0)
        // This matches the real PDK behavior where .put() places the origin, not the box corner.
        double offsetX = comp.NazcaOriginOffsetX;
        double offsetY = comp.NazcaOriginOffsetY;

        // Check if this component actually uses NazcaOriginOffset
        bool usesOriginOffset = (offsetX != 0 || offsetY != 0) ||
                               (IsPdkFunction(funcName) || funcName.StartsWith("demo_pdk.", StringComparison.OrdinalIgnoreCase));

        if (usesOriginOffset && (offsetX != 0 || offsetY != 0))
        {
            // Shifted polygon: corner is at (-offsetX, -offsetY) relative to cell origin
            double x0 = -offsetX;
            double y0 = -offsetY;
            double x1 = w - offsetX;
            double y1 = h - offsetY;

            var px0 = NormalizeZero(x0).ToString("F2", ci);
            var py0 = NormalizeZero(y0).ToString("F2", ci);
            var px1 = NormalizeZero(x1).ToString("F2", ci);
            var py1 = NormalizeZero(y1).ToString("F2", ci);

            sb.AppendLine($"    nd.Polygon(points=[({px0},{py0}),({px1},{py0}),({px1},{py1}),({px0},{py1})], layer=1).put(0, 0)");
        }
        else
        {
            // Legacy/simple components: box at (0,0) to (w,h)
            var ws = w.ToString("F2", ci);
            var hs = h.ToString("F2", ci);
            sb.AppendLine($"    nd.Polygon(points=[(0,0),({ws},0),({ws},{hs}),(0,{hs})], layer=1).put(0, 0)");
        }

        // Generate pins with Nazca coordinates
        // Pin positions must be relative to the cell origin (which may be shifted)
        foreach (var pin in comp.PhysicalPins)
        {
            double pinX, pinY;

            if (usesOriginOffset && (offsetX != 0 || offsetY != 0))
            {
                // Pin position relative to shifted origin
                pinX = pin.OffsetXMicrometers - offsetX;
                pinY = (comp.HeightMicrometers - pin.OffsetYMicrometers) - offsetY;
            }
            else
            {
                // Legacy: pin position in standard Nazca Y-up coordinates
                pinX = pin.OffsetXMicrometers;
                pinY = comp.HeightMicrometers - pin.OffsetYMicrometers;
            }

            var px = NormalizeZero(pinX).ToString("F2", ci);
            var py = NormalizeZero(pinY).ToString("F2", ci);
            var pa = NormalizeZero(-pin.AngleDegrees).ToString("F0", ci);
            sb.AppendLine($"    nd.Pin('{pin.Name}').put({px}, {py}, {pa})");
        }

        sb.AppendLine();
        sb.AppendLine($"def {pythonFuncName}(**kwargs):");
        sb.AppendLine($"    return _{pythonFuncName}_cell");
        sb.AppendLine();
    }

    private static Dictionary<Component, string> AppendComponents(
        StringBuilder sb, DesignCanvasViewModel canvas)
    {
        sb.AppendLine("def create_design():");
        sb.AppendLine("    with nd.Cell(name='ConnectAPIC_Design') as design:");
        sb.AppendLine();
        sb.AppendLine("        # Components");
        var componentNames = new Dictionary<Component, string>();
        int compIndex = 0;
        var ci = CultureInfo.InvariantCulture;

        foreach (var compVm in canvas.Components)
        {
            var comp = compVm.Component;
            if (comp is ComponentGroup group)
            {
                // Flatten group: export all child components at their absolute positions
                foreach (var child in group.GetAllComponentsRecursive())
                    AppendSingleComponent(sb, child, componentNames, ref compIndex, ci);
            }
            else
            {
                AppendSingleComponent(sb, comp, componentNames, ref compIndex, ci);
            }
        }

        sb.AppendLine();
        return componentNames;
    }

    /// <summary>
    /// Appends a single component placement to the Nazca script and records its variable name.
    /// </summary>
    private static void AppendSingleComponent(
        StringBuilder sb, Component comp, Dictionary<Component, string> componentNames,
        ref int compIndex, CultureInfo ci)
    {
        var varName = $"comp_{compIndex}";
        componentNames[comp] = varName;

        var (originOffsetX, originOffsetY) = CalculateOriginOffset(comp);

        var nazcaX = (comp.PhysicalX + originOffsetX).ToString("F2", ci);
        var nazcaY = NormalizeZero(-(comp.PhysicalY + originOffsetY)).ToString("F2", ci);
        var rot = NormalizeZero(-comp.RotationDegrees).ToString("F0", ci);
        var nazcaFunc = GetNazcaFunction(comp);

        // Diagnostic logging (Issue #334): trace coordinate transform for each component.
        // Format: # COORD: <id> editor=(<physX>,<physY>) originOffset=(<ox>,<oy>) nazca=(<nx>,<ny>) rot=<r>
        sb.AppendLine($"        # COORD: {comp.Identifier} " +
                      $"editor=({comp.PhysicalX.ToString("F2", ci)},{comp.PhysicalY.ToString("F2", ci)}) " +
                      $"originOffset=({originOffsetX.ToString("F2", ci)},{originOffsetY.ToString("F2", ci)}) " +
                      $"nazca=({nazcaX},{nazcaY}) rot={rot}");

        // Pin coordinate diagnostics: show expected Nazca pin positions for alignment verification.
        foreach (var pin in comp.PhysicalPins)
        {
            var (pinNazcaX, pinNazcaY) = pin.GetAbsoluteNazcaPosition();
            sb.AppendLine($"        # PIN: {pin.Name} expected_nazca=({pinNazcaX.ToString("F2", ci)},{pinNazcaY.ToString("F2", ci)})");
        }

        // Nazca's Cell.put() defaults to anchoring on the cell's first pin
        // (typically 'a0'), NOT on the cell origin. For demofab components
        // whose 'a0' isn't at (0,0) — e.g. demo.mmi2x2_dp has a0 at y=+4,
        // demo.dbr has a0 at y=-70 — the default anchor shifts the placed
        // cell relative to where Lunima's NazcaOriginOffset math expects.
        // Result: visible Y mismatch in the rendered GDS even though the
        // calibration editor (which reads the same Python-rendered cell)
        // shows alignment as correct.
        //
        // Pin 'org' is the cell-origin marker every demofab/SiEPIC cell
        // ships (set up via bbu.put_boundingbox('org', ...)). Anchoring
        // on 'org' explicitly makes .put() place the cell origin at the
        // computed (x, y) — which IS the contract Lunima's calibration
        // and export math both assume.
        sb.AppendLine($"        {varName} = {nazcaFunc}.put('org', {nazcaX}, {nazcaY}, {rot})  # {comp.Identifier}");
        compIndex++;
    }

    /// <summary>
    /// Calculates the Nazca origin offset for a component based on its type.
    /// </summary>
    private static (double OffsetX, double OffsetY) CalculateOriginOffset(Component comp)
    {
        var funcName = comp.NazcaFunctionName;

        bool hasPdkFunctionName = !string.IsNullOrEmpty(funcName) &&
            (IsPdkFunction(funcName) || funcName.StartsWith("demo_pdk.", StringComparison.OrdinalIgnoreCase));

        // Also use explicit NazcaOriginOffset when set (non-zero), regardless of function name.
        // Fixes components with auto-generated names (e.g. "nazca_grating_coupler") that still
        // have a physical Nazca origin offset. Issue #355.
        bool hasExplicitOriginOffset = comp.NazcaOriginOffsetX != 0 || comp.NazcaOriginOffsetY != 0;

        if (hasPdkFunctionName || hasExplicitOriginOffset)
        {
            double rotRad = comp.RotationDegrees * Math.PI / 180.0;
            double offsetX = comp.NazcaOriginOffsetX * Math.Cos(rotRad) - comp.NazcaOriginOffsetY * Math.Sin(rotRad);
            double offsetY = comp.NazcaOriginOffsetX * Math.Sin(rotRad) + comp.NazcaOriginOffsetY * Math.Cos(rotRad);
            return (offsetX, offsetY);
        }

        if (IsParametricStraight(funcName, comp.NazcaFunctionParameters))
        {
            var firstPin = comp.PhysicalPins.FirstOrDefault();
            if (firstPin != null)
            {
                double rotRad = comp.RotationDegrees * Math.PI / 180.0;
                double offsetX = firstPin.OffsetXMicrometers * Math.Cos(rotRad) - firstPin.OffsetYMicrometers * Math.Sin(rotRad);
                double offsetY = firstPin.OffsetXMicrometers * Math.Sin(rotRad) + firstPin.OffsetYMicrometers * Math.Cos(rotRad);
                return (offsetX, offsetY);
            }
        }

        // Fallback for legacy components with no explicit Nazca origin offset
        return (0, comp.HeightMicrometers);
    }

    private static void AppendConnections(
        StringBuilder sb,
        DesignCanvasViewModel canvas,
        Dictionary<Component, string> componentNames)
    {
        var hasFrozenPaths = canvas.Components.Any(vm => vm.Component is ComponentGroup);
        if (canvas.Connections.Count == 0 && !hasFrozenPaths)
            return;

        sb.AppendLine("        # Waveguide Connections");

        foreach (var connVm in canvas.Connections)
        {
            var conn = connVm.Connection;
            var segments = conn.GetPathSegments();

            if (segments.Count > 0)
                AppendSegmentExport(sb, segments, conn.StartPin, conn.EndPin);
            else
                AppendFallbackExport(sb, conn, componentNames);
        }

        // Export frozen waveguide paths from ComponentGroups
        foreach (var compVm in canvas.Components)
        {
            if (compVm.Component is ComponentGroup group)
                AppendGroupFrozenPaths(sb, group);
        }

        sb.AppendLine();
    }

    /// <summary>
    /// Exports all frozen waveguide paths from a ComponentGroup (and nested groups) as Nazca segments.
    /// </summary>
    private static void AppendGroupFrozenPaths(StringBuilder sb, ComponentGroup group)
    {
        foreach (var frozenPath in group.InternalPaths)
        {
            if (frozenPath?.Path?.Segments?.Count > 0)
                AppendSegmentExport(sb, frozenPath.Path.Segments, frozenPath.StartPin, frozenPath.EndPin);
        }

        foreach (var child in group.ChildComponents)
        {
            if (child is ComponentGroup nestedGroup)
                AppendGroupFrozenPaths(sb, nestedGroup);
        }
    }

    /// <summary>
    /// Appends segment-by-segment Nazca export for a routed connection.
    /// Uses absolute .put(x, y, angle) for EVERY segment to avoid coordinate accumulation errors
    /// that occur with Nazca's chaining syntax (.put() without coordinates).
    ///
    /// Fix #355 (single straight): direct pin-to-pin geometry avoids NazcaOriginOffset mismatch.
    /// Fix #366 (multi-segment): absolute positioning for each segment.
    /// Fix #456 (last segment Y-offset): all segments now use the SAME delta offset derived from
    /// startPin, ensuring a continuous waveguide path with no coordinate-system jump between segments.
    /// Fix #458: straight segment length and angle are recomputed from transformed Nazca endpoints
    /// instead of relying on editor-space stored values, eliminating misalignment for diagonal segments.
    /// All segments use the same uniform offset — no per-segment distortion.
    /// </summary>
    /// <param name="startPin">Start pin used to calculate the editor-to-Nazca offset for all segments.</param>
    /// <param name="endPin">End pin used only for single-straight-segment paths (direct pin-to-pin geometry).</param>
    internal static void AppendSegmentExport(
        StringBuilder sb, IReadOnlyList<PathSegment> segments,
        PhysicalPin? startPin = null, PhysicalPin? endPin = null)
    {
        // Single straight segment: compute geometry directly from both pin positions.
        if (segments.Count == 1 && segments[0] is StraightSegment && startPin != null && endPin != null)
        {
            sb.AppendLine(FormatStraightSegmentFromPins(startPin, endPin));
            return;
        }

        // Multi-segment: Transform all segment endpoints from editor space to Nazca space.
        // The SAME offset is applied to every segment — no per-segment or end-pin correction.
        // The routing path geometry must be preserved exactly; if the endpoint doesn't hit the
        // pin, the issue is in the offset calculation or component placement, not the segments.
        double offsetX = 0;
        double offsetY = 0;
        if (startPin != null && segments.Count > 0)
        {
            var (editorPinX, editorPinY) = startPin.GetAbsolutePosition();
            var (nazcaPinX, nazcaPinY) = startPin.GetAbsoluteNazcaPosition();
            offsetX = nazcaPinX - editorPinX;
            offsetY = nazcaPinY - (-editorPinY);
        }

        for (int i = 0; i < segments.Count; i++)
        {
            double nStartX = segments[i].StartPoint.X + offsetX;
            double nStartY = -segments[i].StartPoint.Y + offsetY;
            double nEndX = segments[i].EndPoint.X + offsetX;
            double nEndY = -segments[i].EndPoint.Y + offsetY;

            sb.AppendLine(FormatSegmentAbsolute(segments[i], nStartX, nStartY, nEndX, nEndY));
        }
    }

    /// <summary>
    /// Formats a path segment (straight or bend) with absolute Nazca positions.
    /// Straight segments compute length and angle from the transformed Nazca endpoints,
    /// ensuring the exported geometry matches the actual endpoint positions.
    /// Bend segments use stored radius/sweep with negated angles for Y-flip.
    /// </summary>
    private static string FormatSegmentAbsolute(
        PathSegment segment, double nazcaStartX, double nazcaStartY,
        double nazcaEndX, double nazcaEndY)
    {
        var ci = CultureInfo.InvariantCulture;
        return segment switch
        {
            StraightSegment => FormatStraightAbsolute(
                nazcaStartX, nazcaStartY, nazcaEndX, nazcaEndY, ci),
            BendSegment bend => FormatBendAbsolute(bend, nazcaStartX, nazcaStartY, ci),
            _ => $"        # Unknown segment type: {segment.GetType().Name}"
        };
    }

    /// <summary>
    /// Formats a straight segment by computing length and angle from Nazca start/end positions.
    /// This is more robust than using stored editor-space angles, because the Nazca Y-flip
    /// is applied to the actual endpoints rather than relying on angle negation.
    /// </summary>
    private static string FormatStraightAbsolute(
        double nazcaStartX, double nazcaStartY,
        double nazcaEndX, double nazcaEndY, CultureInfo ci)
    {
        double dx = nazcaEndX - nazcaStartX;
        double dy = nazcaEndY - nazcaStartY;
        double length = Math.Sqrt(dx * dx + dy * dy);
        double angleDeg = Math.Atan2(dy, dx) * 180.0 / Math.PI;

        var l = length.ToString("F2", ci);
        var x = NormalizeZero(nazcaStartX).ToString("F2", ci);
        var y = NormalizeZero(nazcaStartY).ToString("F2", ci);
        var a = NormalizeZero(angleDeg).ToString("F2", ci);
        return $"        nd.strt(length={l}).put({x}, {y}, {a})";
    }

    /// <summary>
    /// Formats a bend segment with absolute Nazca start position.
    /// The radius is invariant under Y-flip; the sweep angle and start angle are negated.
    /// </summary>
    private static string FormatBendAbsolute(
        BendSegment bend, double nazcaX, double nazcaY, CultureInfo ci)
    {
        var radius = bend.RadiusMicrometers.ToString("F2", ci);
        var sweepAngle = NormalizeZero(-bend.SweepAngleDegrees).ToString("F2", ci);
        var x = NormalizeZero(nazcaX).ToString("F2", ci);
        var y = NormalizeZero(nazcaY).ToString("F2", ci);
        var angle = NormalizeZero(-bend.StartAngleDegrees).ToString("F2", ci);
        return $"        nd.bend(radius={radius}, angle={sweepAngle}).put({x}, {y}, {angle})";
    }

    /// <summary>
    /// Formats a straight waveguide segment using absolute Nazca pin positions.
    /// Computes length and angle from start pin to end pin in Nazca coordinates,
    /// ensuring the waveguide reaches the end pin exactly regardless of NazcaOriginOffset.
    /// Fix for Issue #355: end pin misalignment when NazcaOriginOffsetY ≠ HeightMicrometers.
    /// </summary>
    private static string FormatStraightSegmentFromPins(PhysicalPin startPin, PhysicalPin endPin)
    {
        var ci = CultureInfo.InvariantCulture;
        var (sx, sy) = startPin.GetAbsoluteNazcaPosition();
        var (ex, ey) = endPin.GetAbsoluteNazcaPosition();

        double dx = ex - sx;
        double dy = ey - sy;
        double length = Math.Sqrt(dx * dx + dy * dy);
        double angleDeg = Math.Atan2(dy, dx) * 180.0 / Math.PI;

        var x = NormalizeZero(sx).ToString("F2", ci);
        var y = NormalizeZero(sy).ToString("F2", ci);
        var a = NormalizeZero(angleDeg).ToString("F2", ci);
        var l = length.ToString("F2", ci);

        return $"        nd.strt(length={l}).put({x}, {y}, {a})";
    }

    /// <summary>
    /// Formats a single path segment as a Nazca Python call.
    /// </summary>
    /// <param name="segment">The path segment to format.</param>
    /// <param name="isFirst">If true, includes absolute coordinates; if false, chains with .put().</param>
    /// <param name="startPin">Optional start pin for correct Nazca coordinate calculation (Issue #329 fix)</param>
    internal static string FormatSegment(PathSegment segment, bool isFirst = true, PhysicalPin? startPin = null)
    {
        var ci = CultureInfo.InvariantCulture;

        return segment switch
        {
            StraightSegment straight => FormatStraightSegment(straight, ci, isFirst, startPin),
            BendSegment bend => FormatBendSegment(bend, ci, isFirst, startPin),
            _ => $"        # Unknown segment type: {segment.GetType().Name}"
        };
    }

    /// <summary>
    /// Normalizes negative zero to positive zero to avoid "-0.00" in output.
    /// </summary>
    private static double NormalizeZero(double value) =>
        value == 0.0 ? 0.0 : value;

    private static string FormatStraightSegment(
        StraightSegment straight, CultureInfo ci, bool isFirst, PhysicalPin? startPin = null)
    {
        // For chained segments, use the forward-projected length instead of Euclidean
        // distance. Nazca's nd.strt() goes forward along the propagation direction,
        // so if the segment is slightly diagonal, the Euclidean length would overshoot.
        var length = isFirst
            ? straight.LengthMicrometers
            : ProjectForwardLength(straight);
        var lengthStr = length.ToString("F2", ci);

        if (isFirst)
        {
            double nazcaX;
            double nazcaY;
            if (startPin != null)
            {
                // FIXED (#329, #338): Use correct Nazca pin position accounting for
                // NazcaOriginOffset and component rotation transformation.
                var (pinNazcaX, pinNazcaY) = startPin.GetAbsoluteNazcaPosition();
                nazcaX = pinNazcaX;
                nazcaY = pinNazcaY;
            }
            else
            {
                // Fallback: naive coordinate flip (legacy behavior without pin info)
                nazcaX = straight.StartPoint.X;
                nazcaY = -straight.StartPoint.Y;
            }

            var x = NormalizeZero(nazcaX).ToString("F2", ci);
            var y = NormalizeZero(nazcaY).ToString("F2", ci);
            var angle = NormalizeZero(-straight.StartAngleDegrees).ToString("F2", ci);
            return $"        nd.strt(length={lengthStr}).put({x}, {y}, {angle})";
        }

        return $"        nd.strt(length={lengthStr}).put()";
    }

    /// <summary>
    /// Projects a straight segment's length onto its propagation direction.
    /// Nazca's nd.strt(length=L) goes forward by L along the current angle,
    /// so if the segment is slightly diagonal, we need the forward component only.
    /// </summary>
    private static double ProjectForwardLength(StraightSegment straight)
    {
        double dx = straight.EndPoint.X - straight.StartPoint.X;
        double dy = straight.EndPoint.Y - straight.StartPoint.Y;
        double angleRad = straight.StartAngleDegrees * Math.PI / 180.0;
        double projected = dx * Math.Cos(angleRad) + dy * Math.Sin(angleRad);
        return Math.Max(0, projected);
    }

    private static string FormatBendSegment(BendSegment bend, CultureInfo ci, bool isFirst, PhysicalPin? startPin = null)
    {
        var radius = bend.RadiusMicrometers.ToString("F2", ci);
        var sweepAngle = NormalizeZero(-bend.SweepAngleDegrees).ToString("F2", ci);

        if (isFirst)
        {
            double nazcaX;
            double nazcaY;
            if (startPin != null)
            {
                // FIXED (#329, #338): Use correct Nazca pin position accounting for
                // NazcaOriginOffset and component rotation transformation.
                var (pinNazcaX, pinNazcaY) = startPin.GetAbsoluteNazcaPosition();
                nazcaX = pinNazcaX;
                nazcaY = pinNazcaY;
            }
            else
            {
                nazcaX = bend.StartPoint.X;
                nazcaY = -bend.StartPoint.Y;
            }

            var x = NormalizeZero(nazcaX).ToString("F2", ci);
            var y = NormalizeZero(nazcaY).ToString("F2", ci);
            var angle = NormalizeZero(-bend.StartAngleDegrees).ToString("F2", ci);
            return $"        nd.bend(radius={radius}, angle={sweepAngle}).put({x}, {y}, {angle})";
        }

        return $"        nd.bend(radius={radius}, angle={sweepAngle}).put()";
    }

    private static void AppendFallbackExport(
        StringBuilder sb,
        WaveguideConnection conn,
        Dictionary<Component, string> componentNames)
    {
        var startComp = conn.StartPin.ParentComponent;
        var endComp = conn.EndPin.ParentComponent;

        if (componentNames.TryGetValue(startComp, out var startName) &&
            componentNames.TryGetValue(endComp, out var endName))
        {
            var startPin = conn.StartPin.Name;
            var endPin = conn.EndPin.Name;

            sb.AppendLine(
                $"        ic.sbend_p2p({startName}.pin['{startPin}'], " +
                $"{endName}.pin['{endPin}']).put()");
        }
    }

    private static void AppendFooter(StringBuilder sb)
    {
        sb.AppendLine("    return design");
        sb.AppendLine();
        sb.AppendLine("# Create and export the design");
        sb.AppendLine("design = create_design()");
        sb.AppendLine("design.put()");
        sb.AppendLine();
        sb.AppendLine("# Export GDS with filename matching this script");
        sb.AppendLine("import os");
        sb.AppendLine("import sys");
        sb.AppendLine("script_path = os.path.abspath(__file__)");
        sb.AppendLine("gds_filename = os.path.splitext(script_path)[0] + '.gds'");
        sb.AppendLine("nd.export_gds(filename=gds_filename)");
        sb.AppendLine("print(f'GDS exported to: {gds_filename}')");
    }

    /// <summary>
    /// Returns true if the function name looks like a real PDK function (e.g., "ebeam_y_1550").
    /// Recognizes SiEPIC EBeam PDK naming patterns.
    /// </summary>
    internal static bool IsPdkFunction(string name) =>
        name.StartsWith("ebeam_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("GC_", StringComparison.Ordinal) ||
        name.StartsWith("ANT_", StringComparison.Ordinal) ||
        name.StartsWith("crossing_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("taper_", StringComparison.OrdinalIgnoreCase) ||
        // SiEPIC ships a few generic-named PCells too (the `contra_*` family
        // is the directional-coupler set under the EBeam library).
        name.StartsWith("contra_", StringComparison.OrdinalIgnoreCase) ||
        (name.Contains(".", StringComparison.Ordinal) &&
         !name.StartsWith("demo_pdk.", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Maps a component to its Nazca function call string.
    /// Uses the stored NazcaFunctionName when it's a real PDK function,
    /// falls back to heuristic demofab mapping otherwise.
    /// </summary>
    internal static string GetNazcaFunction(Component comp)
    {
        // Use stored PDK function name if available and looks like a real function
        var funcName = comp.NazcaFunctionName;
        if (!string.IsNullOrEmpty(funcName) && IsPdkFunction(funcName))
        {
            // Keep dots (for module attribute access like demo.mmi2x2_dp), replace other invalid chars
            var pythonFuncName = System.Text.RegularExpressions.Regex.Replace(funcName, @"[^a-zA-Z0-9_.]", "_");

            // Forward stored parameters verbatim — the caller (component model)
            // is responsible for ensuring they match the target PDK function's signature.
            var funcParams = comp.NazcaFunctionParameters;
            if (!string.IsNullOrEmpty(funcParams))
                return $"{pythonFuncName}({funcParams})";
            else
                return $"{pythonFuncName}()";
        }

        // For demo_pdk components, sanitize the function name to a valid Python identifier (replace dots too)
        if (!string.IsNullOrEmpty(funcName) && funcName.StartsWith("demo_pdk.", StringComparison.OrdinalIgnoreCase))
        {
            var pythonFuncName = System.Text.RegularExpressions.Regex.Replace(funcName, @"[^a-zA-Z0-9_]", "_");

            // Skip parameters for stub components - stubs don't support them
            var funcParams = comp.NazcaFunctionParameters;
            bool isParametricStraight = IsParametricStraight(funcName, funcParams);

            if (isParametricStraight && !string.IsNullOrEmpty(funcParams))
                return $"{pythonFuncName}({funcParams})";
            else
                return $"{pythonFuncName}()";
        }

        // Fallback: heuristic mapping to demofab
        var name = funcName?.ToLower() ?? comp.Identifier.ToLower();
        var ci = CultureInfo.InvariantCulture;

        if (name.Contains("straight") || name.Contains("waveguide"))
            return $"demo.shallow.strt(length={comp.WidthMicrometers.ToString(ci)})";
        if (name.Contains("splitter") || name.Contains("1x2"))
            return "demo.mmi1x2_sh()";
        if (name.Contains("grating"))
            return "demo.io()";
        if (name.Contains("coupler") || name.Contains("2x2"))
            return "demo.mmi2x2_dp()";
        if (name.Contains("phase") || name.Contains("shifter"))
            return "demo.eopm_dc(length=500)";
        if (name.Contains("detector") || name.Contains("photo"))
            return "demo.pd()";
        if (name.Contains("bend"))
            return "demo.shallow.bend(angle=90)";
        if (name.Contains("y-junction") || name.Contains("yjunction"))
            return "demo.mmi1x2_sh()";

        return $"demo.shallow.strt(length={comp.WidthMicrometers.ToString(ci)})";
    }
}
