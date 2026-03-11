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
    /// </summary>
    private static void AppendPdkComponentStubs(StringBuilder sb, DesignCanvasViewModel canvas)
    {
        var ci = CultureInfo.InvariantCulture;
        var generated = new HashSet<string>(StringComparer.Ordinal);

        foreach (var compVm in canvas.Components)
        {
            var comp = compVm.Component;
            var funcName = comp.NazcaFunctionName;
            if (string.IsNullOrEmpty(funcName) || !RequiresStub(funcName))
                continue;
            if (!generated.Add(funcName))
                continue; // already generated

            // Check if this is a parametric straight waveguide
            if (IsParametricStraight(funcName, comp.NazcaFunctionParameters))
            {
                AppendParametricStraightStub(sb, funcName, comp, ci);
            }
            else
            {
                AppendStandardComponentStub(sb, funcName, comp, ci);
            }
        }
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

        // Sanitize function name for valid Python identifier (replace dots with underscores)
        var pythonFuncName = funcName.Replace(".", "_");

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
    /// </summary>
    private static void AppendStandardComponentStub(
        StringBuilder sb, string funcName, Component comp, CultureInfo ci)
    {
        var w = comp.WidthMicrometers.ToString("F2", ci);
        var h = comp.HeightMicrometers.ToString("F2", ci);

        // Sanitize function name for valid Python identifier (replace dots with underscores)
        var pythonFuncName = funcName.Replace(".", "_");

        // Define cell once, return cached instance on each call
        sb.AppendLine($"with nd.Cell(name='{funcName}') as _{pythonFuncName}_cell:");
        sb.AppendLine($"    \"\"\"Auto-generated stub for {funcName} ({comp.WidthMicrometers}x{comp.HeightMicrometers} µm).\"\"\"");
        sb.AppendLine($"    nd.Polygon(points=[(0,0),({w},0),({w},{h}),(0,{h})], layer=1).put(0, 0)");

        // Generate pins with Nazca coordinates (Y-up: nazca_y = height - editor_y)
        foreach (var pin in comp.PhysicalPins)
        {
            var px = pin.OffsetXMicrometers.ToString("F2", ci);
            var py = NormalizeZero(comp.HeightMicrometers - pin.OffsetYMicrometers).ToString("F2", ci);
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
            var varName = $"comp_{compIndex}";
            componentNames[comp] = varName;

            // Calculate origin offset based on component type:
            // - PDK components (both real and demo_pdk): use NazcaOriginOffset (rotated if needed)
            // - Parametric straights: calculate from first pin (rotated)
            double originOffsetX = 0;
            double originOffsetY = 0;

            var funcName = comp.NazcaFunctionName;
            if (!string.IsNullOrEmpty(funcName) && (IsPdkFunction(funcName) || funcName.StartsWith("demo_pdk.", StringComparison.OrdinalIgnoreCase)))
            {
                // PDK component (real or demo_pdk): use stored NazcaOriginOffset, accounting for rotation
                double offsetX = comp.NazcaOriginOffsetX;
                double offsetY = comp.NazcaOriginOffsetY;
                double rotRad = comp.RotationDegrees * Math.PI / 180.0;

                originOffsetX = offsetX * Math.Cos(rotRad) - offsetY * Math.Sin(rotRad);
                originOffsetY = offsetX * Math.Sin(rotRad) + offsetY * Math.Cos(rotRad);
            }
            else if (IsParametricStraight(funcName, comp.NazcaFunctionParameters))
            {
                // Parametric straight: offset by rotated pin position
                var firstPin = comp.PhysicalPins.FirstOrDefault();
                if (firstPin != null)
                {
                    double pinLocalX = firstPin.OffsetXMicrometers;
                    double pinLocalY = firstPin.OffsetYMicrometers;
                    double rotRad = comp.RotationDegrees * Math.PI / 180.0;

                    originOffsetX = pinLocalX * Math.Cos(rotRad) - pinLocalY * Math.Sin(rotRad);
                    originOffsetY = pinLocalX * Math.Sin(rotRad) + pinLocalY * Math.Cos(rotRad);
                }
            }
            else
            {
                // Fallback for legacy components: offset by height for Y-flip
                originOffsetY = comp.HeightMicrometers;
            }

            var nazcaX = (comp.PhysicalX + originOffsetX).ToString("F2", ci);
            var nazcaY = NormalizeZero(-(comp.PhysicalY + originOffsetY)).ToString("F2", ci);
            var rot = NormalizeZero(-comp.RotationDegrees).ToString("F0", ci);
            var nazcaFunc = GetNazcaFunction(comp);

            sb.AppendLine($"        {varName} = {nazcaFunc}.put({nazcaX}, {nazcaY}, {rot})  # {comp.Identifier}");
            compIndex++;
        }

        sb.AppendLine();
        return componentNames;
    }

    private static void AppendConnections(
        StringBuilder sb,
        DesignCanvasViewModel canvas,
        Dictionary<Component, string> componentNames)
    {
        if (canvas.Connections.Count == 0)
            return;

        sb.AppendLine("        # Waveguide Connections");
        foreach (var connVm in canvas.Connections)
        {
            var conn = connVm.Connection;
            var segments = conn.GetPathSegments();

            if (segments.Count > 0)
            {
                AppendSegmentExport(sb, segments);
            }
            else
            {
                AppendFallbackExport(sb, conn, componentNames);
            }
        }
        sb.AppendLine();
    }

    /// <summary>
    /// Appends segment-by-segment Nazca export for a routed connection.
    /// First segment uses absolute .put(x, y, angle); subsequent segments
    /// chain with .put() so Nazca auto-connects them without gaps.
    /// </summary>
    internal static void AppendSegmentExport(
        StringBuilder sb, IReadOnlyList<PathSegment> segments)
    {
        for (int i = 0; i < segments.Count; i++)
        {
            sb.AppendLine(FormatSegment(segments[i], isFirst: i == 0));
        }
    }

    /// <summary>
    /// Formats a single path segment as a Nazca Python call.
    /// </summary>
    /// <param name="segment">The path segment to format.</param>
    /// <param name="isFirst">If true, includes absolute coordinates; if false, chains with .put().</param>
    internal static string FormatSegment(PathSegment segment, bool isFirst = true)
    {
        var ci = CultureInfo.InvariantCulture;

        return segment switch
        {
            StraightSegment straight => FormatStraightSegment(straight, ci, isFirst),
            BendSegment bend => FormatBendSegment(bend, ci, isFirst),
            _ => $"        # Unknown segment type: {segment.GetType().Name}"
        };
    }

    /// <summary>
    /// Normalizes negative zero to positive zero to avoid "-0.00" in output.
    /// </summary>
    private static double NormalizeZero(double value) =>
        value == 0.0 ? 0.0 : value;

    private static string FormatStraightSegment(
        StraightSegment straight, CultureInfo ci, bool isFirst)
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
            var x = straight.StartPoint.X.ToString("F2", ci);
            var y = NormalizeZero(-straight.StartPoint.Y).ToString("F2", ci);
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

    private static string FormatBendSegment(BendSegment bend, CultureInfo ci, bool isFirst)
    {
        var radius = bend.RadiusMicrometers.ToString("F2", ci);
        var sweepAngle = NormalizeZero(-bend.SweepAngleDegrees).ToString("F2", ci);

        if (isFirst)
        {
            var x = bend.StartPoint.X.ToString("F2", ci);
            var y = NormalizeZero(-bend.StartPoint.Y).ToString("F2", ci);
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
        sb.AppendLine("nd.export_gds()");
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
            var funcParams = comp.NazcaFunctionParameters;
            return string.IsNullOrEmpty(funcParams)
                ? $"{funcName}()"
                : $"{funcName}({funcParams})";
        }

        // For demo_pdk components, sanitize the function name (dots -> underscores) to call the stub
        if (!string.IsNullOrEmpty(funcName) && funcName.StartsWith("demo_pdk.", StringComparison.OrdinalIgnoreCase))
        {
            var pythonFuncName = funcName.Replace(".", "_");
            var funcParams = comp.NazcaFunctionParameters;
            return string.IsNullOrEmpty(funcParams)
                ? $"{pythonFuncName}()"
                : $"{pythonFuncName}({funcParams})";
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
