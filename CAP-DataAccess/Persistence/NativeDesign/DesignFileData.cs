using CAP_DataAccess.Persistence.PIR;

namespace CAP_DataAccess.Persistence.NativeDesign;

/// <summary>
/// Root data structure for a .lun design file (Photonic Intermediate Representation).
/// Version 2.0 stores S-matrix data, simulation results, metadata, and external references.
/// Legacy v1 files (FormatVersion missing or different) load with a loud warning in the
/// error console and get upgraded to v2.0 on the next save.
/// </summary>
public class DesignFileData
{
    /// <summary>
    /// File format version. "2.0" is the current format. Other values trigger a loud
    /// warning during load; missing PIR sections remain empty until the next save.
    /// </summary>
    public string? FormatVersion { get; set; }

    public List<ComponentData> Components { get; set; } = new();
    public List<ConnectionData> Connections { get; set; } = new();

    /// <summary>
    /// ComponentGroups with their hierarchical structure, frozen paths, and external pins.
    /// </summary>
    public List<DesignGroupData>? Groups { get; set; }

    /// <summary>
    /// Per-component S-matrix data, keyed by component Identifier string.
    /// Null or empty for designs without stored S-matrix overrides.
    /// </summary>
    public Dictionary<string, ComponentSMatrixData>? SMatrices { get; set; }

    /// <summary>
    /// Most recent simulation results and any stored parameter sweep results.
    /// Null if no simulation has been run and saved.
    /// </summary>
    public SimulationResultsData? SimulationResults { get; set; }

    /// <summary>
    /// Design metadata: PDK versions, design rules, authorship.
    /// Automatically populated with dates on every save.
    /// </summary>
    public DesignMetadata? Metadata { get; set; }

    /// <summary>
    /// References to external simulation or measurement files linked to this design.
    /// Null or empty for designs without external data.
    /// </summary>
    public List<ExternalReferenceData>? ExternalReferences { get; set; }

    /// <summary>
    /// Identifier of the coupler designated as THE analysis output for the Eye/BER
    /// and Transient analyses (#754). Null when no coupler is designated (automatic
    /// selection). Older files without this field load with no designation.
    /// </summary>
    public string? AnalysisOutputCoupler { get; set; }

    /// <summary>
    /// GDS-imported component sets scoped to this design (issue #830): the
    /// component drafts AND the source .gds (base64) travel inside the .lun so
    /// it stays self-contained and portable. Null for designs without GDS
    /// imports; older files reference legacy global import PDKs instead and
    /// are migrated on load.
    /// </summary>
    public List<ImportedGdsComponentSetData>? ImportedGdsComponents { get; set; }

    /// <summary>
    /// Per-layer visibility overrides for imported GDS geometry (issue #858).
    /// Only non-default entries are stored; null when every layer is fully
    /// visible. Older files without this field load with all layers shown.
    /// </summary>
    public List<GdsLayerVisibilityData>? LayerVisibility { get; set; }

    /// <summary>
    /// Chip width in micrometers as configured in the Chip Size settings.
    /// Null for files saved before chip-size support was added (defaults to 5000 μm on load).
    /// </summary>
    public double? ChipWidthMicrometers { get; set; }

    /// <summary>
    /// Chip height in micrometers as configured in the Chip Size settings.
    /// Null for files saved before chip-size support was added (defaults to 5000 μm on load).
    /// </summary>
    public double? ChipHeightMicrometers { get; set; }

    /// <summary>
    /// The fabrication process this design is locked to (issue #570 — one process per chip).
    /// Null for legacy files saved before single-process support; migrated on load.
    /// </summary>
    public ActiveProcessData? ActiveProcess { get; set; }

    /// <summary>
    /// Pin-less frozen waveguide paths living directly on the canvas (issue #856):
    /// GDS-imported route geometry released by ungrouping its import group. Null for
    /// files saved before canvas-level frozen paths existed.
    /// </summary>
    public List<CAP_DataAccess.Persistence.DTOs.FrozenPathDto>? CanvasFrozenPaths { get; set; }

    /// <summary>
    /// Coherent interference mode of the Wavelength Spectrum tab (issue #1333): routed
    /// waveguides carry their propagation phase, so arm-length differences show as
    /// fringes. Null in files saved before the mode existed — loads as off.
    /// </summary>
    public bool? CoherentPropagationPhase { get; set; }

    /// <summary>
    /// The workspace (Layout / Optics / Logic / System) the design was last saved in.
    /// Null in files saved before workspaces existed — the design then opens in a
    /// content-based default (see <c>WorkspaceDefaults</c>).
    /// </summary>
    public string? Workspace { get; set; }
}
