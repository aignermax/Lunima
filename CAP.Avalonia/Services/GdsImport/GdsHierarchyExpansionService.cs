using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.GdsImport.DesignScope;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.Interpretation;

namespace CAP.Avalonia.Services.GdsImport;

/// <summary>Exposes real source references from an embedded GDS without segmenting polygons.</summary>
public sealed partial class GdsHierarchyExpansionService(DesignScopedGdsComponentService scope)
{
    /// <summary>Whether this instance belongs to an embedded imported component set.</summary>
    public bool IsImportedComponent(ComponentViewModel selected) =>
        selected.Component is not ComponentGroup && FindSet(selected) is { } set &&
        set.Drafts.FirstOrDefault(d => d.Name == (selected.TemplateName ?? selected.Component.TemplateName))?.ImportedOwnGeometryOnly != true;

    private DesignScopedGdsSet? FindSet(ComponentViewModel selected)
    {
        if (!string.IsNullOrWhiteSpace(selected.TemplatePdkSource))
            return scope.Sets.FirstOrDefault(s => string.Equals(s.PdkName, selected.TemplatePdkSource, StringComparison.OrdinalIgnoreCase));
        // Legacy group/ungroup VMs retain the core template name but lose the VM PDK.
        // Recover only a unique design-scoped template; repeated imports are ambiguous.
        var name = selected.TemplateName ?? selected.Component.TemplateName;
        var matches = scope.Sets.Where(s => s.Drafts.Any(d => d.Name == name)).Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>Prepares the complete replacement. Throws before mutation when preservation is unsafe.</summary>
    public async Task<ExpandImportedGdsHierarchyCommand> PrepareAsync(DesignCanvasViewModel canvas,
        ComponentViewModel selected, CancellationToken ct = default)
    {
        ValidateSelection(canvas, selected);
        var set = FindSet(selected) ?? throw new InvalidOperationException(
            "The original GDS is not embedded in this design. Import the source GDS again to expose its children.");
        var templateName = selected.TemplateName ?? selected.Component.TemplateName;
        var sourceDraft = set.Drafts.SingleOrDefault(d => d.Name == templateName)
            ?? throw new InvalidOperationException("The imported source template is unavailable.");
        var prepared = await Task.Run(async () =>
        {
            using var stream = new MemoryStream(set.GdsBytes, writable: false);
            var library = await new GdsReader().ReadAsync(stream, ct);
            if (sourceDraft.ImportedOwnGeometryOnly)
                throw new InvalidOperationException("This component contains only the source cell's own polygons, with no child references.");
            var names = library.Cells.Keys.Where(n => sourceDraft.ImportedSourceCellName is { } sourceName
                ? n == sourceName : GdsCellDraftMapper.SanitizeComponentName(n) == templateName).ToList();
            if (names.Count != 1)
                throw new InvalidOperationException("The original source cell cannot be identified uniquely. Review and re-import the GDS.");
            var name = names[0];
            var references = library.Cells[name].Elements.OfType<GdsReference>().ToList();
            if (references.Count == 0)
                throw new InvalidOperationException("This source cell contains no child cell references. Its polygons cannot be split into source components.");
            if (references.Any(r => Math.Abs(r.Magnification - 1) > 1e-9))
                throw new InvalidOperationException("This cell contains magnified references. Expansion is unavailable because their geometry cannot yet be preserved.");
            var bbox = new GdsCellFlattener(library).GetBoundingBox(name);
            if (Math.Abs(bbox.Width - sourceDraft.WidthMicrometers) > 1e-6 ||
                Math.Abs(bbox.Height - sourceDraft.HeightMicrometers) > 1e-6)
                throw new InvalidOperationException("The imported template dimensions differ from the source cell. Review and re-import before expanding.");
            var interpretation = new GdsImportInterpretation
            {
                TopCellName = name,
                Cells = set.Interpretation?.Cells.Select(c => c with { ExpandChildren = false }).ToList() ?? []
            };
            var options = new GdsHierarchyImportOptions
            {
                Interpretation = interpretation,
                RouteLayers = [], MetalRouteLayers = [],
                MaxOutlinePointsPerCell = int.MaxValue,
            };
            if (set.Interpretation is not null)
                options = GdsLayerRoleMapping.Apply(options, set.Interpretation.Layers) with
                    { RouteLayers = [], MetalRouteLayers = [] };
            var imported = await GdsHierarchyImporter.ImportAsync(library, name, options, ct);
            if (imported.Instances.Count == 0)
                throw new InvalidOperationException("This cell has no drawable child references to expose.");
            return (imported, bbox);
        }, ct);
        ct.ThrowIfCancellationRequested();
        ValidateSelection(canvas, selected);
        var drafts = new List<PdkComponentDraft>();
        var templates = new Dictionary<string, ComponentTemplate>(StringComparer.Ordinal);
        foreach (var draft in prepared.imported.ImportedCellDrafts)
        {
            var mapped = GdsCellDraftMapper.Map(draft, GdsHierarchyImporter.GdsFileNameToken);
            mapped.ImportedSourceCellName = draft.CellName;
            if (templates.Values.Any(t => t.Name == mapped.Name))
                throw new InvalidOperationException("Child source names collide after normalization. Review and re-import the GDS.");
            var existing = set.Drafts.FirstOrDefault(d => d.Name == mapped.Name);
            if (existing is not null && (existing.WidthMicrometers != mapped.WidthMicrometers ||
                existing.HeightMicrometers != mapped.HeightMicrometers || existing.RawCode != mapped.RawCode))
                throw new InvalidOperationException("A child template conflicts with an existing imported component. Review and re-import the GDS.");
            var chosen = existing ?? mapped;
            if (existing is null) { mapped.ImportedModelStatus = "geometry-only"; drafts.Add(mapped); }
            templates.Add(draft.CellName, PdkTemplateConverter.ConvertToTemplate(chosen, set.PdkName, null));
        }
        var children = CreateChildren(selected.Component, prepared.imported, templates,
            prepared.bbox.Width, prepared.bbox.Height);
        AddOwnGeometry(selected.Component, prepared.imported, children, drafts, set,
            prepared.bbox.Width, prepared.bbox.Height);
        var aliases = PreserveParentPorts(selected.Component, children, templates, prepared.imported, drafts, set);
        var connections = CreateConnections(prepared.imported, children, aliases);
        return new ExpandImportedGdsHierarchyCommand(canvas, selected, children, [], connections,
            () => scope.RegisterExpandedChildren(set, drafts));
    }

    internal static void ValidateSelection(DesignCanvasViewModel canvas, ComponentViewModel selected)
    {
        if (!canvas.Components.Contains(selected) || selected.Component is ComponentGroup)
            throw new InvalidOperationException("Select one imported component on the canvas. Use Ungroup for an existing group.");
        if (selected.IsLocked || selected.Component.ParentGroup is not null)
            throw new InvalidOperationException("Unlock and ungroup this component before exposing its source children.");
        if (canvas.ConnectionManager.Connections.Any(c => c.StartPin.ParentComponent == selected.Component ||
            c.EndPin.ParentComponent == selected.Component))
            throw new InvalidOperationException("This component has external connections. Disconnect them before expanding; expansion cannot yet safely transfer those wires.");
    }
}
