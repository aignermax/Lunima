using System.Globalization;
using System.Text;
using CAP.Avalonia.ViewModels;
using CAP_Core.Components;
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

        // Detect PDK module from components if not explicitly provided
        if (pdkModuleName == null)
            pdkModuleName = DetectPdkModule(canvas);

        AppendHeader(sb, pdkModuleName);
        var componentNames = AppendComponents(sb, canvas);
        AppendConnections(sb, canvas, componentNames);
        AppendFooter(sb);

        return sb.ToString();
    }

    /// <summary>
    /// Detects the PDK module name from component NazcaFunctionNames.
    /// Returns null if only demofab/built-in components are used.
    /// </summary>
    private static string? DetectPdkModule(DesignCanvasViewModel canvas)
    {
        foreach (var compVm in canvas.Components)
        {
            var funcName = compVm.Component.NazcaFunctionName;
            if (!string.IsNullOrEmpty(funcName) && IsPdkFunction(funcName))
                return null; // PDK functions are called directly, module detected in header
        }
        return null;
    }

    private static void AppendHeader(StringBuilder sb, string? pdkModuleName)
    {
        sb.AppendLine("import nazca as nd");
        sb.AppendLine("import nazca.demofab as demo");
        if (!string.IsNullOrEmpty(pdkModuleName))
            sb.AppendLine($"import {pdkModuleName}");
        sb.AppendLine("from nazca.interconnects import Interconnect");
        sb.AppendLine();
        sb.AppendLine("# PDK Configuration");
        sb.AppendLine("WG_WIDTH = 0.45  # Waveguide width in µm");
        sb.AppendLine("BEND_RADIUS = 50  # Minimum bend radius in µm");
        sb.AppendLine();
        sb.AppendLine("# Create interconnect for waveguide routing");
        sb.AppendLine("ic = Interconnect(width=WG_WIDTH, radius=BEND_RADIUS)");
        sb.AppendLine();
        sb.AppendLine("def create_design():");
        sb.AppendLine("    with nd.Cell(name='ConnectAPIC_Design') as design:");
        sb.AppendLine();
    }

    private static Dictionary<Component, string> AppendComponents(
        StringBuilder sb, DesignCanvasViewModel canvas)
    {
        sb.AppendLine("        # Components");
        var componentNames = new Dictionary<Component, string>();
        int compIndex = 0;

        foreach (var compVm in canvas.Components)
        {
            var comp = compVm.Component;
            var varName = $"comp_{compIndex}";
            componentNames[comp] = varName;

            var x = comp.PhysicalX.ToString("F2", CultureInfo.InvariantCulture);
            var y = comp.PhysicalY.ToString("F2", CultureInfo.InvariantCulture);
            var nazcaFunc = GetNazcaFunction(comp);

            sb.AppendLine($"        {varName} = {nazcaFunc}.put({x}, {y})  # {comp.Identifier}");
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
    /// </summary>
    internal static void AppendSegmentExport(
        StringBuilder sb, IReadOnlyList<PathSegment> segments)
    {
        foreach (var segment in segments)
        {
            sb.AppendLine(FormatSegment(segment));
        }
    }

    /// <summary>
    /// Formats a single path segment as a Nazca Python call.
    /// </summary>
    internal static string FormatSegment(PathSegment segment)
    {
        var ci = CultureInfo.InvariantCulture;

        return segment switch
        {
            StraightSegment straight => FormatStraightSegment(straight, ci),
            BendSegment bend => FormatBendSegment(bend, ci),
            _ => $"        # Unknown segment type: {segment.GetType().Name}"
        };
    }

    private static string FormatStraightSegment(
        StraightSegment straight, CultureInfo ci)
    {
        var length = straight.LengthMicrometers.ToString("F2", ci);
        var x = straight.StartPoint.X.ToString("F2", ci);
        var y = straight.StartPoint.Y.ToString("F2", ci);
        var angle = straight.StartAngleDegrees.ToString("F2", ci);

        return $"        nd.strt(length={length}).put({x}, {y}, {angle})";
    }

    private static string FormatBendSegment(BendSegment bend, CultureInfo ci)
    {
        var radius = bend.RadiusMicrometers.ToString("F2", ci);
        var sweepAngle = bend.SweepAngleDegrees.ToString("F2", ci);
        var x = bend.StartPoint.X.ToString("F2", ci);
        var y = bend.StartPoint.Y.ToString("F2", ci);
        var angle = bend.StartAngleDegrees.ToString("F2", ci);

        return $"        nd.bend(radius={radius}, angle={sweepAngle}).put({x}, {y}, {angle})";
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
    /// </summary>
    internal static bool IsPdkFunction(string name) =>
        name.StartsWith("ebeam_", StringComparison.OrdinalIgnoreCase) ||
        name.Contains(".", StringComparison.Ordinal);

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

        // Fallback: heuristic mapping to demofab
        var name = funcName?.ToLower() ?? comp.Identifier.ToLower();

        if (name.Contains("straight") || name.Contains("waveguide"))
            return "demo.shallow.strt(length=250)";
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

        return $"demo.shallow.strt(length={comp.WidthMicrometers.ToString(CultureInfo.InvariantCulture)})";
    }
}
