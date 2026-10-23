using CAP_DataAccess.Persistence.PIR;

namespace CAP_DataAccess.Persistence.NativeDesign;

/// <summary>
/// DTO for a ComponentGroup in the design file.
/// Bridges the UI-layer (TemplateName-based) and core-layer (ComponentGroupDto) serialization.
/// </summary>
public class DesignGroupData
{
    /// <summary>
    /// Group metadata serialized via ComponentGroupSerializer.
    /// </summary>
    public CAP_DataAccess.Persistence.DTOs.ComponentGroupDto GroupDto { get; set; } = new();

    /// <summary>
    /// Child component data with template names for recreation from the component library.
    /// Maps child Identifier to TemplateName.
    /// </summary>
    public List<ChildComponentData> ChildComponents { get; set; } = new();

    /// <summary>
    /// Canvas X position of the group ViewModel.
    /// </summary>
    public double CanvasX { get; set; }

    /// <summary>
    /// Canvas Y position of the group ViewModel.
    /// </summary>
    public double CanvasY { get; set; }

    /// <summary>
    /// Per-chiplet process binding of a top-level group (issue #938): the fabrication
    /// process the chiplet's contents belong to. Null for unbound groups, nested groups
    /// (their scope is the top-level chiplet's), and files saved before per-chiplet
    /// bindings existed — the design-level <see cref="DesignFileData.ActiveProcess"/>
    /// remains the default for those and for ungrouped components.
    /// </summary>
    public ActiveProcessData? ProcessBinding { get; set; }

    /// <summary>
    /// Truth Table pin-role assignment of a top-level group (issue #981): the input,
    /// output, and bias pins plus threshold the panel last successfully extracted with.
    /// Null when the group was never extracted — including every file saved before the
    /// persistence existed — so legacy .lun files stay clean of unused blocks.
    /// </summary>
    public CAP_Core.Components.Core.TruthTablePinAssignment? TruthTablePinAssignment { get; set; }

    /// <summary>
    /// Die definition of a top-level group that is a die of a multi-die assembly
    /// (outline, edge zone, enabled PDKs). Null for ordinary groups and single-die designs.
    /// </summary>
    public DieData? Die { get; set; }
}
