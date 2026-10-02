using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Export;

namespace CAP.Avalonia.Services;

/// <summary>
/// openEBL design-for-test markers for EBeam-only exports (gaps #5/#6 of the openEBL
/// readiness report): an <c>opt_in_TE_1550_device_&lt;name&gt;</c> measurement label on
/// Text (10, 0) at every laser-injection grating coupler — openEBL's functional
/// verification (DFT.xml / <c>run_verification.py</c>) requires one at each GC the
/// measurement laser injects into — and the Floorplan (99, 0) die box around the design.
/// What counts as a laser input reuses the simulation's own notion
/// (<see cref="SimulationService.IsLightSource"/> — the name-based
/// <c>LightSourceClassifier</c> plus the user override — with the laser switched on);
/// a listen-only output coupler (laser off, issue #690) gets no label. Emitted only when
/// <see cref="SiepicEBeamExportProfile"/> resolves, so every other export stays
/// byte-identical; both layers exist in EBeam.lyp, so the submission check's
/// layer-conformity rule stays green.
/// </summary>
internal static class NazcaOpenEblDftWriter
{
    /// <summary>GDS (layer, datatype) of the openEBL measurement labels (EBeam Text layer).</summary>
    private const string OptInLabelLayer = "(10, 0)";

    /// <summary>GDS (layer, datatype) of the openEBL die floorplan box (EBeam Floorplan layer).</summary>
    private const string FloorplanLayer = "(99, 0)";

    /// <summary>openEBL DFT label format: TE polarization at the 1550 nm DFT laser.</summary>
    private const string OptInLabelPrefix = "opt_in_TE_1550_device_";

    /// <summary>openEBL die width in micrometers (run_submission_checks.py).</summary>
    private const double DieWidthMicrometers = 605;

    /// <summary>openEBL die height in micrometers (run_submission_checks.py).</summary>
    private const double DieHeightMicrometers = 410;

    /// <summary>Label device name when no design/file name is known — the export's top cell.</summary>
    private const string FallbackDesignName = "ConnectAPIC_Design";

    /// <summary>
    /// Appends the DFT markers at top-cell level of <c>create_design()</c> (8-space
    /// indentation, like the component placements and connections): one opt_in label per
    /// active laser input at its GC's cell origin (the same 'org' anchor the placement
    /// puts the cell by — distance 0 from the GC, well inside the 10 µm DFT tolerance),
    /// unique per design via a numeric suffix when several GCs inject light, then the
    /// 605 × 410 µm floorplan box whose lower-left corner is the design bbox lower-left,
    /// so the design sits inside it by construction.
    /// </summary>
    /// <param name="sb">The script under construction.</param>
    /// <param name="canvas">The design canvas (groups are flattened, analysis tools ignored).</param>
    /// <param name="designName">
    /// Design/file name for the label; sanitized to <c>[A-Za-z0-9_]</c>. Null or blank
    /// falls back to <see cref="FallbackDesignName"/>.
    /// </param>
    public static void AppendDftMarkers(StringBuilder sb, DesignCanvasViewModel canvas, string? designName)
    {
        var components = canvas.Components
            .SelectMany(vm => Flatten(vm.Component))
            .Where(c => !c.IsAnalysisTool)
            .ToList();
        if (components.Count == 0)
            return;

        var ci = CultureInfo.InvariantCulture;
        sb.AppendLine("        # openEBL design-for-test markers (SiEPIC DFT)");
        AppendOptInLabels(sb, components, SanitizeDesignName(designName), ci);
        AppendFloorplanBox(sb, components, ci);
    }

    /// <summary>Restricts a raw name to the DFT device-name alphabet <c>[A-Za-z0-9_]</c>.</summary>
    internal static string SanitizeDesignName(string? designName)
    {
        if (string.IsNullOrWhiteSpace(designName))
            return FallbackDesignName;
        return Regex.Replace(designName, "[^A-Za-z0-9_]", "_");
    }

    private static void AppendOptInLabels(
        StringBuilder sb, IReadOnlyList<Component> components, string deviceName, CultureInfo ci)
    {
        var labelCount = 0;
        foreach (var comp in components)
        {
            if (!SimulationService.IsLightSource(comp) || !comp.LaserEnabled)
                continue;

            labelCount++;
            var label = labelCount == 1 ? deviceName : $"{deviceName}_{labelCount}";
            var origin = NazcaCoordinateMapper.GetCellPlacement(comp, rawOverrideAnchor: null);
            sb.AppendLine(
                $"        nd.Annotation(text='{OptInLabelPrefix}{label}', layer={OptInLabelLayer})" +
                $".put({origin.X.ToString("F2", ci)}, {origin.Y.ToString("F2", ci)})");
        }
    }

    private static void AppendFloorplanBox(
        StringBuilder sb, IReadOnlyList<Component> components, CultureInfo ci)
    {
        // App space is Y-down, nazca Y-up (plain negation): the nazca bbox lower-left is
        // (min PhysicalX, negated max bottom edge).
        var x0 = components.Min(c => c.PhysicalX);
        var y0 = -components.Max(c => c.PhysicalY + c.HeightMicrometers);
        var x1 = x0 + DieWidthMicrometers;
        var y1 = y0 + DieHeightMicrometers;

        var px0 = NazcaCoordinateMapper.NormalizeZero(x0).ToString("F2", ci);
        var py0 = NazcaCoordinateMapper.NormalizeZero(y0).ToString("F2", ci);
        var px1 = NazcaCoordinateMapper.NormalizeZero(x1).ToString("F2", ci);
        var py1 = NazcaCoordinateMapper.NormalizeZero(y1).ToString("F2", ci);
        sb.AppendLine(
            $"        nd.Polygon(points=[({px0},{py0}),({px1},{py0}),({px1},{py1}),({px0},{py1})], " +
            $"layer={FloorplanLayer}).put(0, 0)  # openEBL die floorplan 605 x 410 um");
    }

    private static IEnumerable<Component> Flatten(Component component) =>
        component is ComponentGroup group
            ? group.GetAllComponentsRecursive()
            : new[] { component };
}
