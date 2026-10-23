using System.Globalization;
using System.Text;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.Routing;
using CAP_Core.Routing.InterconnectRouting;
using CAP_Core.Routing.MetalRouting;
using static CAP.Avalonia.Services.NazcaExport.NazcaComponentPlacementWriter;
using static CAP.Avalonia.Services.NazcaExport.NazcaComponentStubWriter;
using static CAP.Avalonia.Services.NazcaExport.NazcaConnectionWriter;
using static CAP.Avalonia.Services.NazcaExport.NazcaScriptEnvelope;
using CAP.Avalonia.Services.NazcaExport;

namespace CAP.Avalonia.Services;

/// <summary>
/// Simple Nazca exporter for the physical coordinate system.
/// Exports components and waveguide connections to Python/Nazca code.
/// </summary>
public class SimpleNazcaExporter
{
    /// <summary>
    /// GDS (layer, datatype) pair for pin/port labels (TEXT elements), emitted as a
    /// Python layer tuple: the gdsfactory port-label convention (1, 10), which is also
    /// the default our GDS re-import pin detector reads
    /// (<c>GdsPinDetectionOptions.PortLayers</c>, issue #808). Labels are emitted as
    /// <c>nd.Annotation</c> — <c>nd.text</c> renders stroked POLYGONS, not GDS TEXT
    /// records, so a label-based pin detector would never see it.
    /// Internal so <see cref="NazcaRawCodeCellWriter"/> labels raw-code pins identically.
    /// </summary>
    internal const string PortLabelLayer = NazcaPythonText.PortLabelLayer;

    /// <summary>
    /// Optional source of global interconnect settings (waveguide width/bend radius/GDS layer,
    /// issue #574). When null, the historical export defaults (<see cref="InterconnectSettings"/>)
    /// are used.
    /// </summary>
    public Func<InterconnectSettings>? SettingsSource { get; set; }

    /// <summary>
    /// Exports the full design to a Python/Nazca script.
    /// Component stub cells carry a TEXT label per optical pin and the design's external
    /// ports (fiber couplers) carry top-cell labels — both on the gdsfactory port-label
    /// layer (1, 10) so the GDS re-import detects named pins (issue #808). Group outline
    /// polygons (GDS-imported background geometry) export as nd.Polygon on their original
    /// (layer, datatype) — see <see cref="NazcaOutlinePolygonWriter"/>.
    /// A design built entirely from the SiEPIC EBeam PDK routes its waveguide
    /// interconnect on the EBeam cross-section stamped onto its pins (width + Si layer)
    /// instead of the nazca default layer and drops the demofab bb_body frame —
    /// see <see cref="SiepicEBeamExportProfile"/> — additionally carries the
    /// openEBL design-for-test markers (opt_in label, floorplan box) of
    /// <see cref="NazcaOpenEblDftWriter"/>, and wraps every routed optical
    /// connection AND every optical frozen path (group-internal or canvas-level)
    /// in its own SiEPIC waveguide cell (Waveguide 1/99 guide + DevRec
    /// + PinRec pins, <see cref="SiepicWaveguideCellWriter"/>).
    /// </summary>
    /// <param name="canvas">The design canvas to export.</param>
    /// <param name="pdkModuleName">Optional PDK module name (e.g., "siepic_ebeam_pdk") for import.</param>
    /// <param name="emitVerification">
    /// When true, appends a machine-readable verification epilog (issue #565) that dumps
    /// every placed instance's ACTUAL world pin positions — reported by the same nazca
    /// engine that writes the GDS — to '&lt;script&gt;.pins.json' next to the script.
    /// </param>
    /// <param name="metalSpec">
    /// Process-derived metal routing parameters for electrical connections (issue #682):
    /// trace width, GDS layer/datatype, and waveguide-crossing policy. Electrical
    /// connections are emitted as metal on that layer instead of as optical waveguides.
    /// Null uses <see cref="MetalRoutingSpec.Default"/>.
    /// </param>
    /// <param name="skippedConnections">
    /// Optional collector: appended with an "Start.Pin → End.Pin" description for every
    /// connection or frozen group path left out of the geometry because its route is a
    /// placeholder or invalid (<see cref="ExportableConnections.IsExportable(RoutedPath?)"/>).
    /// Populated as a side effect of THIS write, so the caller's post-export report always
    /// matches what actually landed in the script — a separately recomputed snapshot could
    /// diverge if routing is still running in the background.
    /// </param>
    /// <param name="unresolvedCrossings">
    /// Optional collector: appended with an "Start.Pin → End.Pin" description for every
    /// EXPORTED optical connection <c>WaveguideConnectionManager</c>'s sibling-crossing pass
    /// flagged (<see cref="RoutedPath.IsBlockedFallback"/>) and that a bridge marker does not
    /// resolve — the geometry is rendered (a real, non-placeholder crossing is not a reason to
    /// omit it), but the layout still deserves a second look.
    /// </param>
    /// <param name="library">
    /// Optional component library for raw-code inlining (<see cref="NazcaRawCodeCellWriter"/>):
    /// placed components whose template carries nazca-backend raw code (GDS imports, custom
    /// Python cells) then export their REAL geometry instead of a box stub / demofab
    /// heuristic. Null keeps the legacy behavior.
    /// </param>
    /// <param name="exportWarnings">
    /// Optional collector: appended with one description per UNIQUE raw-code template
    /// whose geometry source is missing (a deleted .gds file) and that therefore exports
    /// as a placeholder box stub — a template placed ten times warns once, matching the
    /// once-per-template fallback emission.
    /// </param>
    /// <param name="designName">
    /// Optional design/file name used for the openEBL opt_in measurement labels of an
    /// EBeam-only export (<see cref="NazcaOpenEblDftWriter"/>); null falls back to the
    /// top-cell name. Ignored for every other design.
    /// </param>
    public string Export(
        DesignCanvasViewModel canvas,
        string? pdkModuleName = null,
        bool emitVerification = false,
        MetalRoutingSpec? metalSpec = null,
        List<string>? skippedConnections = null,
        List<string>? unresolvedCrossings = null,
        IEnumerable<ComponentTemplate>? library = null,
        List<string>? exportWarnings = null,
        string? designName = null)
    {
        // A die assembly exports one cell per die under an assembly top cell; single-die
        // designs continue below unchanged.
        if (canvas.Dies.IsAssemblyRoot)
            return ExportAssembly(canvas, onlyDie: null, metalSpec, skippedConnections, library, exportWarnings);

        var sb = new StringBuilder();
        var metal = metalSpec ?? MetalRoutingSpec.Default;
        var interconnectSettings = SettingsSource?.Invoke() ?? new InterconnectSettings();
        var ebeamProfile = SiepicEBeamExportProfile.Resolve(canvas);
        if (ebeamProfile != null)
            interconnectSettings = ebeamProfile.ApplyTo(interconnectSettings);
        var interconnectPlan = NazcaProcessInterconnectPlan.Build(canvas, interconnectSettings);
        var rawCodePlan = NazcaRawCodeCellWriter.BuildPlan(canvas, include: null, library, exportWarnings);
        var wrapperPlan = NazcaPinLabelWrapperWriter.BuildPlan(canvas, include: null, rawCodePlan, library);

        AppendHeader(sb, interconnectSettings, metal);
        interconnectPlan.AppendTo(sb);
        interconnectPlan.Companions.AppendDefinitions(sb);
        AppendPdkComponentStubs(sb, canvas, include: null, rawCodePlan, wrapperPlan, ebeamProfile != null);
        NazcaRawCodeCellWriter.AppendCells(sb, rawCodePlan, CultureInfo.InvariantCulture);
        NazcaPinLabelWrapperWriter.AppendCells(sb, wrapperPlan);
        var componentNames = AppendComponents(sb, canvas, emitVerification, rawCodePlan: rawCodePlan, wrapperPlan: wrapperPlan);
        NazcaOutlinePolygonWriter.AppendGroupOutlinePolygons(sb, canvas);
        var waveguideCells = ebeamProfile != null
            ? new SiepicWaveguideCellWriter(ebeamProfile.WidthMicrometers)
            : null;
        AppendConnections(
            sb, canvas, componentNames, metal, interconnectSettings.GdsLayer,
            skippedConnections, unresolvedCrossings, interconnectPlan, ebeamProfile != null, waveguideCells);
        if (ebeamProfile != null)
            NazcaOpenEblDftWriter.AppendDftMarkers(sb, canvas, designName);
        AppendFooter(sb);
        SiepicCellUpgradeWriter.AppendUpgradeBlock(sb, canvas, addDevRec: ebeamProfile != null);
        // The spine pass must run after the foundry-cell upgrade: its pin matching
        // reads the real foundry PinRec paths from the upgraded GDS.
        waveguideCells?.AppendSpineBlock(sb);
        if (emitVerification)
            AppendVerificationEpilog(sb);

        return sb.ToString();
    }

    /// <summary>
    /// Exports only the components matching <paramref name="include"/> — the nazca-native
    /// group of a mixed-backend export. Connections are NOT emitted: routed
    /// waveguides are owned by the main gdsfactory script, which imports the partial GDS
    /// this script renders and merges it into the final output. The top cell is named
    /// <paramref name="topCellName"/> so it cannot collide with the gdsfactory design cell.
    /// </summary>
    /// <param name="canvas">The design canvas to export.</param>
    /// <param name="include">Predicate selecting the components to render.</param>
    /// <param name="topCellName">Name of the partial design's top cell.</param>
    /// <param name="metalSpec">Metal routing parameters; null uses <see cref="MetalRoutingSpec.Default"/>.</param>
    /// <param name="library">
    /// Optional component library for raw-code inlining — see <see cref="Export"/>.
    /// </param>
    /// <param name="exportWarnings">
    /// Optional collector for missing-source raw-code fallbacks — see <see cref="Export"/>.
    /// </param>
    public string ExportPartial(
        DesignCanvasViewModel canvas,
        Func<Component, bool> include,
        string topCellName,
        MetalRoutingSpec? metalSpec = null,
        IEnumerable<ComponentTemplate>? library = null,
        List<string>? exportWarnings = null)
    {
        var sb = new StringBuilder();
        var metal = metalSpec ?? MetalRoutingSpec.Default;
        var interconnectSettings = SettingsSource?.Invoke() ?? new InterconnectSettings();
        var rawCodePlan = NazcaRawCodeCellWriter.BuildPlan(canvas, include, library, exportWarnings);
        var wrapperPlan = NazcaPinLabelWrapperWriter.BuildPlan(canvas, include, rawCodePlan, library);

        AppendHeader(sb, interconnectSettings, metal);
        AppendPdkComponentStubs(sb, canvas, include, rawCodePlan, wrapperPlan);
        NazcaRawCodeCellWriter.AppendCells(sb, rawCodePlan, CultureInfo.InvariantCulture);
        NazcaPinLabelWrapperWriter.AppendCells(sb, wrapperPlan);
        AppendComponents(sb, canvas, emitVerification: false, include, topCellName, rawCodePlan, wrapperPlan);
        NazcaOutlinePolygonWriter.AppendGroupOutlinePolygons(sb, canvas, include);
        AppendFooter(sb);
        SiepicCellUpgradeWriter.AppendUpgradeBlock(sb, canvas, include);

        return sb.ToString();
    }

    /// <summary>GDS layer of the die outline frame (documentation/floorplan layer).</summary>
    public const int DieFrameLayer = NazcaDieAssemblyWriter.DieFrameLayer;

    /// <summary>Exports all fabrication dies, or one selected die in its local coordinate frame.</summary>
    public string ExportAssembly(
        DesignCanvasViewModel canvas,
        ComponentGroup? onlyDie = null,
        MetalRoutingSpec? metalSpec = null,
        List<string>? skippedConnections = null,
        IEnumerable<ComponentTemplate>? library = null,
        List<string>? exportWarnings = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var settings = SettingsSource?.Invoke() ?? new InterconnectSettings();
        return NazcaDieAssemblyWriter.ExportAssembly(
            canvas, settings, onlyDie, metalSpec, skippedConnections, library, exportWarnings);
    }

    /// <summary>Python-safe cell name for a die ("Die 2" → "Die_2").</summary>
    public static string DieCellName(ComponentGroup die) => NazcaDieAssemblyWriter.DieCellName(die);

    // Preserve the internal entry points consumed by existing writers and contract tests.
    internal static string EscapePythonString(string value) => NazcaPythonText.EscapePythonString(value);

    internal static string SanitizePythonComment(string value) => NazcaPythonText.SanitizePythonComment(value);

    internal static string GetNazcaFunction(Component comp) => NazcaComponentFunctionResolver.GetNazcaFunction(comp);

    internal static string FormatSegment(PathSegment segment, bool isFirst = true, PhysicalPin? startPin = null) =>
        NazcaChainedSegmentFormatter.FormatSegment(segment, isFirst, startPin);

    internal static void AppendSegmentExport(
        StringBuilder sb, IReadOnlyList<PathSegment> segments,
        PhysicalPin? startPin = null, PhysicalPin? endPin = null,
        MetalTraceStyle? metal = null, (int Layer, int DataType)? sourceLayer = null,
        ProcessCrossSection crossSection = default, string? companionXsection = null) =>
        NazcaSegmentWriter.AppendSegmentExport(
            sb, segments, startPin, endPin, metal, sourceLayer, crossSection, companionXsection);
}
