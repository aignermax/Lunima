using System.Globalization;
using System.Text;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using static CAP.Avalonia.Services.NazcaExport.NazcaComponentFunctionResolver;
using static CAP.Avalonia.Services.NazcaExport.NazcaPythonText;
using static CAP.Avalonia.Services.NazcaExport.NazcaScriptEnvelope;

namespace CAP.Avalonia.Services.NazcaExport;

/// <summary>Places flattened design components and records their names and external optical ports.</summary>
internal static class NazcaComponentPlacementWriter
{
    internal static Dictionary<Component, string> AppendComponents(
        StringBuilder sb, DesignCanvasViewModel canvas, bool emitVerification = false,
        Func<Component, bool>? include = null, string topCellName = "ConnectAPIC_Design",
        RawCodeExportPlan? rawCodePlan = null, PinLabelWrapperPlan? wrapperPlan = null,
        string functionName = "create_design")
    {
        sb.AppendLine($"def {functionName}():");
        sb.AppendLine($"    with nd.Cell(name='{topCellName}') as design:");
        sb.AppendLine();
        sb.AppendLine("        # Components");
        var componentNames = new Dictionary<Component, string>();
        int compIndex = 0;
        var ci = CultureInfo.InvariantCulture;
        var plan = rawCodePlan ?? RawCodeExportPlan.Empty;

        foreach (var compVm in canvas.Components)
        {
            var comp = compVm.Component;
            if (comp.IsAnalysisTool) continue;
            // gdsfactory-native components (e.g. CornerStone SiN) have no Nazca representation —
            // skip them rather than emit a meaningless demofab stub. They export via the
            // gdsfactory export instead. Only in the full export, though: when an include
            // predicate partitions the design (mixed-backend partial), it alone decides —
            // skipping here would silently drop raw-code nazca components that also carry a
            // gdsfactory function name.
            if (include == null && !string.IsNullOrEmpty(comp.GdsFactoryFunction)) continue;
            if (comp is ComponentGroup group)
            {
                // Flatten group: export all child components at their absolute positions.
                // GetAllComponentsRecursive lists nested group NODES too — a group is not a
                // physical cell and placing it would emit a demofab heuristic box on top of
                // its (also listed) leaf children.
                foreach (var child in group.GetAllComponentsRecursive())
                {
                    if (child.IsAnalysisTool) continue;
                    if (child is ComponentGroup) continue;
                    if (include == null && !string.IsNullOrEmpty(child.GdsFactoryFunction)) continue;
                    if (include != null && !include(child)) continue;
                    AppendSingleComponent(sb, child, componentNames, ref compIndex, ci, plan, wrapperPlan);
                }
            }
            else
            {
                if (include != null && !include(comp)) continue;
                AppendSingleComponent(sb, comp, componentNames, ref compIndex, ci, plan, wrapperPlan);
            }
        }

        if (emitVerification)
            AppendVerificationRegistry(sb, componentNames);

        sb.AppendLine();
        return componentNames;
    }

    /// <summary>
    /// Appends a single component placement to the Nazca script and records its variable name.
    /// </summary>
    private static void AppendSingleComponent(
        StringBuilder sb, Component comp, Dictionary<Component, string> componentNames,
        ref int compIndex, CultureInfo ci, RawCodeExportPlan? rawCodePlan = null,
        PinLabelWrapperPlan? wrapperPlan = null)
    {
        var varName = $"comp_{compIndex}";

        var placement = NazcaCoordinateMapper.GetCellPlacement(comp, rawOverrideAnchor: null);
        var nazcaX = placement.X.ToString("F2", ci);
        var nazcaY = placement.Y.ToString("F2", ci);
        var rot = placement.RotationDegrees.ToString("F0", ci);
        // A raw-code component (GDS import / custom Python cell) calls its inlined
        // wrapper — same call shape for the real-geometry wrapper and the
        // missing-source fallback box stub (<see cref="NazcaRawCodeCellWriter"/>).
        // A pin-label-wrapped component (electrical pins on a real module call)
        // calls its wrapper cell (<see cref="NazcaPinLabelWrapperWriter"/>).
        var nazcaFunc = rawCodePlan is not null && rawCodePlan.TryGetEntry(comp, out var rawEntry)
            ? $"{rawEntry.FunctionName}()"
            : wrapperPlan is not null && wrapperPlan.TryGetEntry(comp, out var wrapperEntry)
                ? $"{wrapperEntry.FunctionName}()"
                : GetNazcaFunction(comp);

        // Diagnostic logging (Issue #334): trace coordinate transform for each component.
        // originOffset is the effective put-position offset relative to the editor
        // top-left, derived from the mapper placement so the diagnosis can never
        // drift from the emitted coordinates.
        double originOffsetX = NazcaCoordinateMapper.NormalizeZero(placement.X - comp.PhysicalX);
        double originOffsetY = NazcaCoordinateMapper.NormalizeZero(-placement.Y - comp.PhysicalY);
        sb.AppendLine($"        # COORD: {comp.Identifier} " +
                      $"editor=({comp.PhysicalX.ToString("F2", ci)},{comp.PhysicalY.ToString("F2", ci)}) " +
                      $"originOffset=({originOffsetX.ToString("F2", ci)},{originOffsetY.ToString("F2", ci)}) " +
                      $"nazca=({nazcaX},{nazcaY}) rot={rot}");

        // Pin coordinate diagnostics: show expected Nazca pin positions for alignment verification.
        foreach (var pin in comp.PhysicalPins)
        {
            var (pinNazcaX, pinNazcaY) = NazcaCoordinateMapper.GetPinNazcaPosition(pin);
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
        var flip = placement.Flip ? ", flip=True" : "";
        sb.AppendLine($"        {varName} = {nazcaFunc}.put('org', {nazcaX}, {nazcaY}, {rot}{flip})  # {comp.Identifier}");

        // External ports of the design get a top-cell label on the port-label layer,
        // so re-imports and label-based tools find the circuit's interface (#808).
        AppendExternalPortLabels(sb, comp, ci);

        // Record the variable only after its put-line was emitted: a half-failed append
        // must not leave a name pointing at a component that was never placed.
        componentNames[comp] = varName;
        compIndex++;
    }

    /// <summary>
    /// Emits one top-cell port label (GDS TEXT on <see cref="PortLabelLayer"/>) per optical
    /// pin of a fiber-interface coupler — the design's external ports. What counts as a
    /// coupler follows <see cref="LightSourceClassifier"/> (grating/edge couplers), the same
    /// single source of truth the simulation uses to bind external inputs/outputs: both
    /// laser-enabled input couplers and listen-only output couplers are labeled. The label
    /// sits at the pin's world position (the same plain-Y-negation transform as every other
    /// pin coordinate in this script) and is named "{Identifier}_{PinName}" so multiple
    /// couplers never produce colliding port names. Non-coupler components emit nothing:
    /// their pins are already labeled inside their stub cells.
    /// </summary>
    private static void AppendExternalPortLabels(StringBuilder sb, Component comp, CultureInfo ci)
    {
        if (!LightSourceClassifier.IsLightInjectingCoupler(comp))
            return;

        foreach (var pin in comp.PhysicalPins)
        {
            // Optical pins only — an electrical pin (e.g. a detector's contacts) is not
            // an optical port and must not become one on re-import (#519).
            if (pin.MatterType != MatterType.Light) continue;

            var (x, y) = NazcaCoordinateMapper.GetPinNazcaPosition(pin);
            var px = x.ToString("F2", ci);
            var py = y.ToString("F2", ci);
            var portName = EscapePythonString($"{comp.Identifier}_{pin.Name}");
            sb.AppendLine($"        nd.Annotation(text='{portName}', layer={PortLabelLayer}).put({px}, {py})");
        }
    }
}
