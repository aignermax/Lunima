using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels.Library;

/// <summary>
/// The inputs of a design-wide PDK-source resolution, captured on the UI thread so the
/// resolution itself can run on a worker thread. <see cref="Capture"/> only copies
/// references — placed components with their template snapshots, every nested group child
/// and the library templates; the template lookups, which scale with the gate count, happen
/// in <see cref="Resolve"/>, which reads nothing but the captured lists.
/// </summary>
public sealed class ComponentPdkSourceSnapshot
{
    private readonly IReadOnlyList<PlacedComponent> _placed;
    private readonly IReadOnlyList<Component> _groupChildren;
    private readonly IReadOnlyList<ComponentTemplate> _templates;

    private ComponentPdkSourceSnapshot(IReadOnlyList<PlacedComponent> placed,
        IReadOnlyList<Component> groupChildren, IReadOnlyList<ComponentTemplate> templates) =>
        (_placed, _groupChildren, _templates) = (placed, groupChildren, templates);

    /// <summary>
    /// Captures the canvas state the resolution needs. Must run on the UI thread: it reads the
    /// canvas collection and walks the groups' live child lists.
    /// </summary>
    /// <param name="placed">The top-level components on the canvas.</param>
    /// <param name="groups">The top-level groups among them; their children are captured at every depth.</param>
    /// <param name="templates">A snapshot of the library templates; kept by reference, not copied.</param>
    public static ComponentPdkSourceSnapshot Capture(IEnumerable<ComponentViewModel> placed,
        IEnumerable<ComponentGroup> groups, IReadOnlyList<ComponentTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(placed);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(templates);
        return new ComponentPdkSourceSnapshot(
            placed.Select(vm => new PlacedComponent(vm.Component, vm.TemplatePdkSource)).ToList(),
            groups.SelectMany(group => group.GetAllComponentsRecursive()).ToList(),
            templates);
    }

    /// <summary>
    /// Resolves every captured component's PDK source the way the placement and paste guards
    /// do: a placed component's template snapshot, else its live-library match; group children
    /// carry no snapshot of their own and are matched against the library. Safe on any thread.
    /// </summary>
    /// <returns>Each component's PDK source name, null where no template matches.</returns>
    /// <exception cref="ArgumentException">The same component was placed twice on the canvas.</exception>
    public IReadOnlyDictionary<Component, string?> Resolve()
    {
        var sources = new Dictionary<Component, string?>(_placed.Count + _groupChildren.Count);
        foreach (var placed in _placed)
            sources.Add(placed.Component, placed.TemplatePdkSource ?? ResolveFromLibrary(placed.Component));
        foreach (var child in _groupChildren)
        {
            if (!sources.ContainsKey(child))
                sources.Add(child, ResolveFromLibrary(child));
        }
        return sources;
    }

    private string? ResolveFromLibrary(Component component) =>
        ComponentPdkSourceResolver.Resolve(component, _templates);

    private readonly record struct PlacedComponent(Component Component, string? TemplatePdkSource);
}
