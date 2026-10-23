using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core;
using CAP_Core.Components.Core;
using CAP_DataAccess.Persistence.PIR;

namespace CAP.Avalonia.ViewModels.Panels.DesignModels;

/// <summary>Owns project model overrides, their migration, and precedence on placed components.</summary>
public sealed class DesignModelOverrides : IDisposable
{
    private readonly ObservableCollection<ComponentViewModel> _components;
    private readonly ComponentTemplateResolver _templates;
    private readonly ITemplateSMatrixStore? _global;
    private readonly ErrorConsoleService? _errors;
    private readonly Func<IEnumerable<Component>> _documentRoots;

    /// <summary>Attaches model application to the existing placement collection.</summary>
    public DesignModelOverrides(ObservableCollection<ComponentViewModel> components,
        ComponentTemplateResolver templates, ITemplateSMatrixStore? global, ErrorConsoleService? errors,
        Func<IEnumerable<Component>>? documentRoots = null)
    {
        _components = components;
        _templates = templates;
        _global = global;
        _errors = errors;
        _documentRoots = documentRoots ?? (() => components.Select(view => view.Component));
        _components.CollectionChanged += OnComponentsChanged;
    }

    /// <summary>Stable project-local dictionary shared with model import consumers.</summary>
    public Dictionary<string, ComponentSMatrixData> Stored { get; } = new();

    /// <summary>Applies global overrides without changing project-local stored values.</summary>
    public void ApplyGlobal(IEnumerable<Component> components)
    {
        if (_global == null || _global.Overrides.Count == 0) return;
        SMatrixOverrideApplicator.ApplyAll(components, _global.Overrides,
            templateKeyResolver: _templates.Key, errorConsole: _errors,
            keyMatchesKnownTemplate: _templates.ContainsKey);
    }

    /// <summary>Applies global models first and project instance models last.</summary>
    public void Apply(IEnumerable<Component> components, bool reportOrphans = false)
    {
        var snapshot = components.ToList();
        ApplyGlobal(snapshot);
        if (Stored.Count > 0)
            SMatrixOverrideApplicator.ApplyAll(snapshot, Stored,
                templateKeyResolver: _templates.Key, geometryKeyResolver: ComponentGeometryKey.For,
                errorConsole: _errors, keyMatchesKnownTemplate: _templates.ContainsKey,
                reportOrphans: reportOrphans);
    }

    /// <summary>Restores project models, migrating legacy template keys to the global store.</summary>
    public int Restore(Dictionary<string, ComponentSMatrixData>? saved)
    {
        Stored.Clear();
        var migrated = 0;
        foreach (var (key, value) in saved ?? new Dictionary<string, ComponentSMatrixData>())
        {
            if (_global != null && key.Contains("::", StringComparison.Ordinal))
            {
                _global.Overrides[key] = value;
                migrated++;
            }
            else Stored[key] = value;
        }
        if (migrated > 0)
        {
            _global!.Save();
            _errors?.LogWarning($"Migrated {migrated} PDK template S-matrix override(s) from project file to user-global storage. " +
                "These now apply to all projects. Save this project to finalise the migration.");
        }
        Apply(_components.Select(vm => vm.Component), reportOrphans: saved != null);
        return migrated;
    }

    /// <summary>Captures reachable models using the existing geometry/identifier collection policy.</summary>
    public Dictionary<string, ComponentSMatrixData> Capture()
    {
        var hierarchy = CAP_Core.Analysis.Hierarchy.DesignHierarchy.From(_documentRoots());
        var live = hierarchy.Leaves.Concat<Component>(hierarchy.Groups).ToArray();
        return SMatrixOverrideGc.Sweep(Stored, live.Select(ComponentGeometryKey.For).ToHashSet(),
            live.Select(component => component.Identifier).ToHashSet());
    }

    private void OnComponentsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems == null) return;
        var added = args.NewItems.OfType<ComponentViewModel>().Select(vm => vm.Component).ToList();
        if (added.Count > 0) Apply(added);
    }

    /// <summary>Detaches the placement subscription when its design owner is retired.</summary>
    public void Dispose() => _components.CollectionChanged -= OnComponentsChanged;
}
