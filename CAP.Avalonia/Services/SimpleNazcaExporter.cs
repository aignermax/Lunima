using System.Globalization;
using System.Text;
using CAP.Avalonia.ViewModels;
using CAP_Core.Components;

namespace CAP.Avalonia.Services;

/// <summary>
/// Simple Nazca exporter for the physical coordinate system.
/// Exports components and waveguide connections to Python/Nazca code.
/// </summary>
public class SimpleNazcaExporter
{
    public string Export(DesignCanvasViewModel canvas)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("import nazca as nd");
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

        // Export components
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

        // Export connections
        if (canvas.Connections.Count > 0)
        {
            sb.AppendLine("        # Waveguide Connections");
            foreach (var connVm in canvas.Connections)
            {
                var conn = connVm.Connection;
                var startComp = conn.StartPin.ParentComponent;
                var endComp = conn.EndPin.ParentComponent;

                if (componentNames.TryGetValue(startComp, out var startName) &&
                    componentNames.TryGetValue(endComp, out var endName))
                {
                    var startPin = conn.StartPin.Name;
                    var endPin = conn.EndPin.Name;

                    sb.AppendLine($"        ic.strt_p2p({startName}.pin['{startPin}'], {endName}.pin['{endPin}']).put()");
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("    return design");
        sb.AppendLine();
        sb.AppendLine("# Create and export the design");
        sb.AppendLine("design = create_design()");
        sb.AppendLine("design.put()");
        sb.AppendLine("nd.export_gds()");

        return sb.ToString();
    }

    private string GetNazcaFunction(Component comp)
    {
        // Map component type to Nazca function
        var name = comp.NazcaFunctionName?.ToLower() ?? comp.Identifier.ToLower();

        if (name.Contains("straight") || name.Contains("waveguide"))
            return "nd.strt(length=250)";
        if (name.Contains("splitter") || name.Contains("1x2"))
            return "nd.mmi1x2()";
        if (name.Contains("coupler") || name.Contains("2x2"))
            return "nd.mmi2x2()";
        if (name.Contains("phase") || name.Contains("shifter"))
            return "nd.eopm(length=500)";
        if (name.Contains("grating"))
            return "nd.grating()";
        if (name.Contains("detector") || name.Contains("photo"))
            return "nd.pd()";
        if (name.Contains("bend"))
            return "nd.bend(angle=90)";
        if (name.Contains("y-junction") || name.Contains("yjunction"))
            return "nd.Yjunction()";

        // Default: generic component
        return $"nd.strt(length={comp.WidthMicrometers.ToString(CultureInfo.InvariantCulture)})";
    }
}
