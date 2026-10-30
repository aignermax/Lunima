using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using CAP_DataAccess.Persistence.NativeDesign;
using CAP.Avalonia.ViewModels.Panels.DesignModels;

namespace CAP.Avalonia.ViewModels.Panels.NativeDesign;

/// <summary>Maps native design and chiplet process bindings against the live installed catalog.</summary>
public sealed class NativeProcessBindings
{
    private readonly ComponentTemplateResolver _templates;
    private readonly Func<IReadOnlyList<ProcessGroup>> _catalog;
    private readonly Func<IReadOnlyCollection<string>> _agnostic;
    private readonly Action<string> _warn;
    /// <summary>Creates the native process-binding mapper with live catalog providers.</summary>
    public NativeProcessBindings(ComponentTemplateResolver templates,
        Func<IReadOnlyList<ProcessGroup>> catalog, Func<IReadOnlyCollection<string>> agnostic,
        Action<string> warn)
    {
        _templates = templates;
        _catalog = catalog;
        _agnostic = agnostic;
        _warn = warn;
    }

    /// <summary>
    /// Serialized form of a top-level group's chiplet process binding (issue #938): the
    /// explicitly set binding, or — for groups built without one (e.g. in Playground) —
    /// the binding derived from the children's PDK sources when they unanimously belong
    /// to one catalog process. Null when the group has no process content, its children
    /// span processes, or no catalog is available.
    /// </summary>
    public ActiveProcessData? CaptureGroup(ComponentGroup group)
    {
        var binding = group.ProcessBinding;
        if (binding == null && _catalog() is { Count: > 0 } catalog)
        {
            binding = GroupProcessPolicy.DeriveProcessBinding(
                group.GetAllComponentsRecursive().Select(_templates.PdkSource),
                catalog,
                _agnostic() ?? Array.Empty<string>());
        }
        return ActiveProcessResolver.ToData(binding);
    }

    /// <summary>
    /// Restores a top-level group's persisted chiplet process binding (issue #938),
    /// re-anchored to the installed process catalog exactly like the design-level
    /// record: newly installed compatible PDKs join the binding, and a chiplet whose
    /// PDKs are all missing warns instead of silently pinning a nonexistent process.
    /// Null for unbound groups and legacy files.
    /// </summary>
    public ActiveProcessSelection? RestoreGroup(DesignGroupData groupData)
    {
        var binding = ActiveProcessResolver.FromData(groupData.ProcessBinding);
        if (binding is not { IsPlayground: false })
            return binding;

        var catalog = _catalog() ?? Array.Empty<ProcessGroup>();
        var revalidated = ActiveProcessResolver.Revalidate(binding, catalog, out var warning);
        if (warning != null)
        {
            _warn(
                $"Chiplet '{groupData.GroupDto.GroupName}': {warning}");
        }
        return revalidated;
    }
    /// <summary>Restores the design binding, including legacy inference from unbound content.</summary>
    public void RestoreDesign(DesignFileData designData, IEnumerable<Component> components,
        Action<ActiveProcessSelection?> applySelection)
    {
        ActiveProcessSelection? selection;
        // Restore the active process (issue #570), or infer one for legacy files
        // that predate single-process support from the placed components' PDKs.
        var storedProcess = ActiveProcessResolver.FromData(designData.ActiveProcess);
        if (storedProcess != null)
        {
            // Re-anchor the stored snapshot to the installed catalog: compatible
            // PDKs installed since the save join the process, and a design whose
            // PDKs are missing warns instead of silently bricking the library.
            var installedCatalog = _catalog() ?? Array.Empty<ProcessGroup>();
            selection = ActiveProcessResolver.Revalidate(
                storedProcess, installedCatalog, out var revalidationWarning);
            applySelection(selection);
            if (revalidationWarning != null)
                _warn(revalidationWarning);
        }
        else
        {
            var catalog = _catalog() ?? Array.Empty<ProcessGroup>();
            // Chiplets with a restored binding carry their own process (issue
            // #938): their children stay out of the design-level inference,
            // which is only the default for ungrouped and unbound content.
            var pdkSources = designData.Components.Select(c => c.PdkSource)
                .Concat(designData.Groups?
                        .Where(g => g.ProcessBinding == null)
                        .SelectMany(g => g.ChildComponents.Select(ch => ch.PdkSource))
                        ?? Enumerable.Empty<string?>());
            selection = ActiveProcessResolver.Migrate(pdkSources, catalog, out var warning,
                _agnostic() ?? System.Array.Empty<string>());
            applySelection(selection);
            if (warning != null) _warn(warning);

            // No unbound process content: the design default follows the
            // chiplets themselves — one shared process, or Playground for a
            // genuine multi-process carrier (no migration, hence no warning).
            if (selection == null)
                applySelection(ActiveProcessResolver.FromChipletBindings(
                    components.OfType<ComponentGroup>().Select(g => g.ProcessBinding)));
        }

    }

}
