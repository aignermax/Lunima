using CAP_Core;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Process;
using CAP_Core.Routing;
using CAP_DataAccess.Persistence;
using CAP_DataAccess.Persistence.NativeDesign;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Converters;
using CAP.Avalonia.ViewModels.Panels.DesignModels;

namespace CAP.Avalonia.ViewModels.Panels.NativeDesign;

/// <summary>Captures group hierarchy and child component state for native design files.</summary>
public sealed class NativeGroupCapture
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentTemplateResolver _templates;
    /// <summary>Creates group capture using explicit canvas and template identity adapters.</summary>
    public NativeGroupCapture(DesignCanvasViewModel canvas, ComponentTemplateResolver templates)
    {
        _canvas = canvas;
        _templates = templates;
    }
    /// <summary>Captures top-level and nested group records in dependency order.</summary>
    public List<DesignGroupData> Capture(IEnumerable<ComponentViewModel> groups,
        Func<ComponentGroup, ActiveProcessData?> resolveBinding)
    {
        var result = new List<DesignGroupData>();
        foreach (var group in groups) SerializeGroupRecursively(group, result, resolveBinding);
        return result;
    }

    /// <summary>
    /// Recursively serializes a ComponentGroup and all its nested child groups.
    /// Adds each group (including nested ones) to the groups list.
    /// </summary>
    private void SerializeGroupRecursively(ComponentViewModel groupVm, List<DesignGroupData> groupsList, Func<ComponentGroup, ActiveProcessData?> resolveBinding)
    {
        var group = (ComponentGroup)groupVm.Component;

        // First, recursively serialize any nested child groups
        foreach (var child in group.ChildComponents)
        {
            if (child is ComponentGroup childGroup)
            {
                // Find the VM for this child group (if it exists on canvas)
                // For nested groups, they won't have their own VM on canvas
                // We'll create a minimal representation
                var childVm = _canvas.Components.FirstOrDefault(c => c.Component == child);
                if (childVm != null)
                {
                    SerializeGroupRecursively(childVm, groupsList, resolveBinding);
                }
                else
                {
                    // Nested group - serialize it with its physical position
                    SerializeNestedGroup(childGroup, groupsList, resolveBinding);
                }
            }
        }

        // Then serialize this group itself
        var groupDto = ComponentGroupSerializer.ToDto(group);
        var childDataList = new List<ChildComponentData>();
        CollectChildComponentData(group, childDataList);

        groupsList.Add(new DesignGroupData
        {
            GroupDto = groupDto,
            ChildComponents = childDataList,
            CanvasX = groupVm.X,
            CanvasY = groupVm.Y,
            // Only top-level groups act as chiplets (issue #938); a nested group's
            // process scope is its top-level parent's.
            ProcessBinding = group.ParentGroup == null ? resolveBinding(group) : null,
            // Truth Table pin roles persist on every group: a gate nested inside a
            // cell instance must keep its assignment or hierarchical designs lose
            // their gates on save.
            TruthTablePinAssignment = group.TruthTablePinAssignment?.Copy(),
            Die = group.ParentGroup == null ? DieData.From(group.Die) : null
        });
    }

    /// <summary>
    /// Serializes a nested ComponentGroup that doesn't have its own canvas VM.
    /// </summary>
    private void SerializeNestedGroup(ComponentGroup group, List<DesignGroupData> groupsList, Func<ComponentGroup, ActiveProcessData?> resolveBinding)
    {
        // First, recursively serialize any nested child groups
        foreach (var child in group.ChildComponents)
        {
            if (child is ComponentGroup childGroup)
            {
                SerializeNestedGroup(childGroup, groupsList, resolveBinding);
            }
        }

        // Then serialize this group
        var groupDto = ComponentGroupSerializer.ToDto(group);
        var childDataList = new List<ChildComponentData>();
        CollectChildComponentData(group, childDataList);

        groupsList.Add(new DesignGroupData
        {
            GroupDto = groupDto,
            ChildComponents = childDataList,
            CanvasX = group.PhysicalX,
            CanvasY = group.PhysicalY,
            // A nested gate keeps its Truth Table pin roles like a top-level one.
            TruthTablePinAssignment = group.TruthTablePinAssignment?.Copy()
        });
    }

    /// <summary>
    /// Collects child component data (with template names) from a group.
    /// Only collects direct children that are NOT ComponentGroups (nested groups are serialized separately).
    /// </summary>
    private void CollectChildComponentData(
        ComponentGroup group, List<ChildComponentData> childDataList)
    {
        foreach (var child in group.ChildComponents)
        {
            if (child is ComponentGroup)
            {
                // Skip nested groups - they have their own DesignGroupData entry
                continue;
            }

            var templateName = _templates.Name(child);
            var pdkSource = _templates.PdkSource(child);

            childDataList.Add(new ChildComponentData
            {
                Identifier = child.Identifier,
                ComponentGuid = child.Id.ToString(),
                TemplateName = templateName,
                PdkSource = pdkSource,
                X = child.PhysicalX,
                Y = child.PhysicalY,
                Rotation = (int)child.Rotation90CounterClock,
                RotationDegrees = ComponentPoseTransform.GetNonCardinalRotationDegrees(child),
                Mirrored = child.IsMirroredHorizontally ? true : null,
                IsBackground = child.IsRoutingObstacle ? null : true,
                SliderValue = child.GetAllSliders().Count > 0
                    ? child.GetSlider(0)?.Value : null,
                SliderValues = NativeComponentStateMapper.SnapshotSliderValues(child),
                IsLocked = child.IsLocked ? true : null,
                HumanReadableName = child.HumanReadableName
            });
        }
    }

    /// <summary>
    /// Recursively collects all child component identifiers from a group.
    /// </summary>
    public static void CollectChildIds(ComponentGroup group, HashSet<string> ids)
    {
        foreach (var child in group.ChildComponents)
        {
            ids.Add(child.Identifier);
            if (child is ComponentGroup nested)
            {
                CollectChildIds(nested, ids);
            }
        }
    }
}
