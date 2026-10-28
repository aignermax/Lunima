using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Process;
using CAP_DataAccess.Persistence;
using CAP_DataAccess.Persistence.NativeDesign;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels.DesignModels;

namespace CAP.Avalonia.ViewModels.Panels.NativeDesign;

/// <summary>Maps native design grouploader state.</summary>
public sealed class NativeGroupLoader
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentTemplateResolver _templates;

    /// <summary>Creates the group reconstruction adapter for the current canvas.</summary>
    public NativeGroupLoader(DesignCanvasViewModel canvas, ComponentTemplateResolver templates)
    {
        _canvas = canvas;
        _templates = templates;
    }

    /// <summary>
    /// Loads ComponentGroups from saved design data, handling nested groups correctly.
    /// Creates child components first, then reconstructs groups in dependency order.
    /// </summary>
    public int LoadGroups(List<DesignGroupData> groupDataList,
        Func<DesignGroupData, ActiveProcessSelection?> restoreBinding, List<string> failedGroups)
    {
        // Primary lookup: by saved Guid (prevents name-collision bugs when copying groups).
        // Fallback lookup: by Identifier string (for old files that predate Guid fields).
        var guidLookup = new Dictionary<Guid, Component>();
        var nameFallback = new Dictionary<string, Component>();

        // First pass: Create all non-group child components
        foreach (var groupData in groupDataList)
        {
            foreach (var childData in groupData.ChildComponents)
            {
                // Determine the lookup key for this child
                var hasGuid = childData.ComponentGuid != null
                              && Guid.TryParse(childData.ComponentGuid, out var childGuid);

                // Skip if already created under the same key
                if (hasGuid && guidLookup.ContainsKey(Guid.Parse(childData.ComponentGuid!)))
                    continue;
                if (!hasGuid && nameFallback.ContainsKey(childData.Identifier))
                    continue;

                var template = _templates.Find(childData.TemplateName, childData.PdkSource);

                if (template == null)
                    continue;

                var child = ComponentTemplates.CreateFromTemplate(
                    template, childData.X, childData.Y);

                // Restore human-readable name
                child.Identifier = childData.Identifier;
                ComponentTemplates.ReserveIdentifier(childData.Identifier);

                // Restore HumanReadableName
                if (childData.HumanReadableName != null)
                    child.HumanReadableName = childData.HumanReadableName;

                NativeComponentStateMapper.RestorePose(child, childData.Mirrored, childData.Rotation, childData.RotationDegrees);
                // Before the group registers its obstacles: background never blocks routing.
                if (childData.IsBackground == true)
                    child.IsRoutingObstacle = false;

                // Restore slider values (all sliders; legacy single value as fallback)
                NativeComponentStateMapper.RestoreSliderValues(child, childData.SliderValues, childData.SliderValue);

                if (childData.IsLocked == true)
                    child.IsLocked = true;

                // Index by saved Guid (primary) and by name (fallback for old files)
                if (hasGuid)
                    guidLookup[Guid.Parse(childData.ComponentGuid!)] = child;
                nameFallback[child.Identifier] = child;
            }
        }

        // Second pass: Reconstruct groups in dependency order (children before parents)
        var orderedGroups = NativeGroupOrder.TopologicalSortGroups(groupDataList);

        foreach (var groupData in orderedGroups)
        {
            // Reconstruct the group using Guid-based lookup with name fallback.
            // A corrupted group (dangling pin/component references) must not abort
            // the load into an empty canvas — skip it, keep loading, report afterwards.
            ComponentGroup group;
            try
            {
                group = ComponentGroupSerializer.FromDto(
                    groupData.GroupDto, guidLookup, nameFallback);
            }
            catch (Exception ex)
            {
                failedGroups.Add($"'{groupData.GroupDto.GroupName}': {ex.Message}");
                continue;
            }

            // Index the group itself so nested parents can find it
            if (groupData.GroupDto.IdGuid != null
                && Guid.TryParse(groupData.GroupDto.IdGuid, out var groupGuid))
            {
                guidLookup[groupGuid] = group;
            }
            nameFallback[group.Identifier] = group;

            // Truth Table pin roles: restore as persisted on every group — a gate
            // nested inside a cell instance keeps its roles; the panel silently
            // skips pin names that no longer match a real external pin.
            group.TruthTablePinAssignment = groupData.TruthTablePinAssignment;

            // Only add top-level groups (groups without a parent) to the canvas
            if (groupData.GroupDto.ParentGroupId == null)
            {
                group.ProcessBinding = restoreBinding(groupData);
                var groupVm = _canvas.AddComponent(group);
                groupVm.X = groupData.CanvasX;
                groupVm.Y = groupData.CanvasY;
                group.PhysicalX = groupData.CanvasX;
                group.PhysicalY = groupData.CanvasY;
                if (groupData.Die != null)
                {
                    CAP_Core.Components.Dies.DieOrigin.Assign(group, groupData.Die.ToDefinition()
                        ?? CAP_Core.Components.Dies.LegacyDieMigration.DefinitionFromContent(group));
                    groupVm.X = group.PhysicalX;
                    groupVm.Y = group.PhysicalY;
                    groupVm.NotifyDimensionsChanged();
                }
            }
        }

        return orderedGroups.Count - failedGroups.Count;
    }
}
