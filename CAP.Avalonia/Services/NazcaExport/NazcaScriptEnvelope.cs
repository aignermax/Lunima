using System.Globalization;
using System.Text;
using CAP.Avalonia.Services.MetalRouting;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.Routing.InterconnectRouting;
using CAP_Core.Routing.MetalRouting;

namespace CAP.Avalonia.Services.NazcaExport;

/// <summary>Nazca script configuration, GDS export entry point, and alignment-verification scaffolding.</summary>
internal static class NazcaScriptEnvelope
{
    internal static void AppendHeader(StringBuilder sb, InterconnectSettings settings, MetalRoutingSpec metal)
    {
        var ci = CultureInfo.InvariantCulture;
        sb.AppendLine("import nazca as nd");
        sb.AppendLine("import nazca.demofab as demo");
        sb.AppendLine("from nazca.interconnects import Interconnect");
        sb.AppendLine();
        sb.AppendLine("# PDK Configuration");
        sb.AppendLine($"WG_WIDTH = {settings.WidthMicrometers.ToString("0.0###", ci)}  # Waveguide width in µm");
        sb.AppendLine($"BEND_RADIUS = {settings.BendRadiusMicrometers.ToString("0.###", ci)}  # Minimum bend radius in µm");
        if (settings.GdsLayer.HasValue)
            sb.AppendLine($"WG_LAYER = {settings.GdsLayer.Value}  # Waveguide GDS layer");
        sb.AppendLine();
        NazcaMetalTraceWriter.AppendHeaderConstants(sb, metal);
        sb.AppendLine("# Create interconnect for waveguide routing");
        sb.AppendLine(settings.GdsLayer.HasValue
            ? "ic = Interconnect(width=WG_WIDTH, radius=BEND_RADIUS, layer=WG_LAYER)"
            : "ic = Interconnect(width=WG_WIDTH, radius=BEND_RADIUS)");
        sb.AppendLine();
    }

    internal static void AppendFooter(StringBuilder sb)
    {
        sb.AppendLine("    return design");
        sb.AppendLine();
        sb.AppendLine("# Create and export the design");
        sb.AppendLine("design = create_design()");
        sb.AppendLine();
        sb.AppendLine("# Export GDS with filename matching this script");
        sb.AppendLine("import os");
        sb.AppendLine("import sys");
        sb.AppendLine("script_path = os.path.abspath(__file__)");
        sb.AppendLine("gds_filename = os.path.splitext(script_path)[0] + '.gds'");
        // topcells=[design]: plain nd.export_gds() would export the default 'nazca'
        // cell tree only, so the design had to be instantiated under it (design.put())
        // and the written GDS's sole top cell was the empty 'nazca' wrapper around
        // ConnectAPIC_Design — a re-import then offered only that wrapper as the
        // top-cell candidate and exploded into ONE black box instead of the placed
        // components. Exporting the design cell directly keeps it the GDS top cell.
        sb.AppendLine("nd.export_gds(topcells=[design], filename=gds_filename)");
        sb.AppendLine("print(f'GDS exported to: {gds_filename}')");
    }

    /// <summary>
    /// Exposes the placed instances to the verification epilog. The comp_N variables
    /// are locals of create_design(), but the epilog runs at module level after the
    /// GDS export — a module-level registry bridges the two scopes.
    /// </summary>
    internal static void AppendVerificationRegistry(
        StringBuilder sb, Dictionary<Component, string> componentNames)
    {
        var pairs = string.Join(", ", componentNames.Values.Select(n => $"('{n}', {n})"));
        sb.AppendLine();
        sb.AppendLine("        # Instance registry for the alignment verification epilog.");
        sb.AppendLine("        global _verify_instances");
        sb.AppendLine($"        _verify_instances = [{pairs}]");
    }

    /// <summary>
    /// Emits the self-verification footer (issue #565): the TRUE world pin positions of
    /// every placed instance, asked from the same nazca engine that wrote the GDS — the
    /// GDS itself carries no pins. The result is written as JSON next to the script so
    /// tests (and tooling) can compare it against <see cref="NazcaCoordinateMapper"/>.
    /// </summary>
    internal static void AppendVerificationEpilog(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("# --- Alignment verification (machine-readable) ---");
        sb.AppendLine("import json as _json");
        sb.AppendLine("_verify = {}");
        sb.AppendLine("for _name, _inst in _verify_instances:");
        sb.AppendLine("    _pins = {}");
        sb.AppendLine("    for _pn, _pin in _inst.pin.items():");
        sb.AppendLine("        _px, _py, _pa = _pin.xya()");
        sb.AppendLine("        # float() unwraps numpy scalars, which json cannot serialize.");
        sb.AppendLine("        _pins[_pn] = [float(_px), float(_py), float(_pa)]");
        sb.AppendLine("    _verify[_name] = _pins");
        sb.AppendLine("with open(os.path.splitext(script_path)[0] + '.pins.json', 'w') as _f:");
        sb.AppendLine("    _json.dump(_verify, _f)");
    }
}
