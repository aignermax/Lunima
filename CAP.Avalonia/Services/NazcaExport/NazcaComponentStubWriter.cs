using System.Globalization;
using System.Text;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using static CAP.Avalonia.Services.NazcaExport.NazcaComponentStubGeometry;

namespace CAP.Avalonia.Services.NazcaExport;

/// <summary>Selects and deduplicates PDK stubs, honoring real raw-code and pin-label wrapper cells.</summary>
internal static class NazcaComponentStubWriter
{
    /// <summary>
    /// Generates standalone Nazca cell definitions for PDK components.
    /// Each unique PDK function AND parameter set used in the design gets a stub cell
    /// with correct dimensions and pin positions — no external PDK install needed
    /// (parameterized names carry a hash, <see cref="NazcaStubNaming"/>, issue #783).
    /// ComponentGroups are flattened — stubs are generated for all child components.
    /// SiEPIC stubs are placeholders only: after <c>nd.export_gds()</c> the script's
    /// klayout post-pass (<see cref="SiepicCellUpgradeWriter"/>) swaps their content
    /// for the real foundry geometry when the PDK is installed.
    /// </summary>
    internal static void AppendPdkComponentStubs(
        StringBuilder sb, DesignCanvasViewModel canvas, Func<Component, bool>? include = null,
        RawCodeExportPlan? rawCodePlan = null, PinLabelWrapperPlan? wrapperPlan = null,
        bool omitBlackBoxFrame = false)
    {
        var ci = CultureInfo.InvariantCulture;
        var generated = new HashSet<string>(StringComparer.Ordinal);
        var plan = rawCodePlan ?? RawCodeExportPlan.Empty;

        foreach (var compVm in canvas.Components)
        {
            var comp = compVm.Component;
            if (comp.IsAnalysisTool) continue;
            if (comp is ComponentGroup group)
            {
                foreach (var child in group.GetAllComponentsRecursive())
                {
                    if (child.IsAnalysisTool) continue;
                    if (include != null && !include(child)) continue;
                    AppendComponentStub(sb, child, generated, ci, plan, wrapperPlan, omitBlackBoxFrame);
                }
            }
            else
            {
                if (include != null && !include(comp)) continue;
                AppendComponentStub(sb, comp, generated, ci, plan, wrapperPlan, omitBlackBoxFrame);
            }
        }
    }

    /// <summary>
    /// Generates a PDK stub for a single component if required.
    /// Dedupes by STUB name, not function name: parameterized components carry a
    /// parameters hash in the name (issue #783), so each distinct parameter set
    /// generates its own stub while identical placements still share one.
    /// A component in the raw-code plan renders its real geometry via
    /// <see cref="NazcaRawCodeCellWriter"/> instead — no stub — unless it is a
    /// missing-source fallback, which keeps a box stub under the wrapper's
    /// function name so the placement call is identical either way.
    /// </summary>
    private static void AppendComponentStub(
        StringBuilder sb, Component comp, HashSet<string> generated, CultureInfo ci,
        RawCodeExportPlan plan, PinLabelWrapperPlan? wrapperPlan = null,
        bool omitBlackBoxFrame = false)
    {
        if (plan.TryGetEntry(comp, out var rawEntry))
        {
            if (!rawEntry.IsFallback || !generated.Add(rawEntry.FunctionName))
                return;
            AppendStandardComponentStub(
                sb, rawEntry.Template.Name, rawEntry.FunctionName, comp, ci,
                NazcaCoordinateMapper.GetStubAnchor(comp));
            return;
        }

        // Pin-label-wrapped components place the real module cell inside the
        // wrapper — the (dead, never-called) box stub would be pure noise.
        if (wrapperPlan is not null && wrapperPlan.TryGetEntry(comp, out _))
            return;

        var funcName = comp.NazcaFunctionName;
        if (string.IsNullOrEmpty(funcName) || !RequiresStub(funcName))
            return;
        var stubName = NazcaStubNaming.StubName(funcName, comp.NazcaFunctionParameters);
        if (!generated.Add(stubName))
            return;

        if (NazcaCoordinateMapper.IsParametricStraight(funcName, comp.NazcaFunctionParameters))
            AppendParametricStraightStub(sb, funcName, comp, ci, omitBlackBoxFrame);
        else
            AppendStandardComponentStub(sb, funcName, stubName, comp, ci);
    }

    /// <summary>
    /// Checks if a function requires a stub definition.
    /// Returns true for real PDK functions and demo_pdk functions.
    /// </summary>
    private static bool RequiresStub(string funcName) =>
        NazcaCoordinateMapper.IsPdkFunction(funcName) ||
        funcName.StartsWith("demo_pdk.", StringComparison.OrdinalIgnoreCase);
}
