using System.Collections.Concurrent;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Routing.WaveguideCompanions;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP.Avalonia.Services.WaveguideCompanions;

/// <summary>
/// Resolves the process companion layers (cladding / trench / keep-out bands) of one routed
/// waveguide from its endpoint components' PDKs, falling back to the design-wide (active
/// process) spec when neither endpoint belongs to a loaded PDK. Canvas rendering and the
/// Nazca export share this provider so the canvas shows exactly the bands the GDS gets.
/// The component → PDK lookup scans the library, so it is cached per component function
/// and dropped whenever the library size changes.
/// </summary>
public sealed class WaveguideCompanionLayerProvider
{
    private readonly Func<IReadOnlyCollection<ComponentTemplate>> _templates;
    private readonly Func<IReadOnlyList<PdkDraft>> _drafts;
    private readonly Func<WaveguideCompanionSpec> _designWide;
    private readonly ConcurrentDictionary<string, string?> _pdkByFunction = new(StringComparer.Ordinal);
    private int _cachedTemplateCount = -1;

    private const long DesignWideMemoMilliseconds = 250;
    private WaveguideCompanionSpec? _designWideMemo;
    private long _designWideMemoAt;

    /// <summary>Creates the provider over live library / PDK sources.</summary>
    /// <param name="templates">The component library (to resolve a component's PDK).</param>
    /// <param name="drafts">The loaded PDK drafts (their processes declare the layers).</param>
    /// <param name="designWide">The active process' spec, used when no endpoint PDK resolves.</param>
    public WaveguideCompanionLayerProvider(
        Func<IReadOnlyCollection<ComponentTemplate>> templates,
        Func<IReadOnlyList<PdkDraft>> drafts,
        Func<WaveguideCompanionSpec> designWide)
    {
        _templates = templates;
        _drafts = drafts;
        _designWide = designWide;
    }

    /// <summary>The companion spec for a waveguide between <paramref name="startPin"/> and <paramref name="endPin"/>.</summary>
    public WaveguideCompanionSpec Resolve(PhysicalPin? startPin, PhysicalPin? endPin)
    {
        var templates = _templates();
        if (templates.Count != _cachedTemplateCount)
        {
            _pdkByFunction.Clear();
            _cachedTemplateCount = templates.Count;
        }

        var spec = WaveguideCompanionSpecResolver.ResolveForEndpointPdkNames(
            PdkOf(startPin?.ParentComponent, templates),
            PdkOf(endPin?.ParentComponent, templates),
            _drafts());
        return spec ?? DesignWide();
    }

    // The design-wide lookup re-derives the live process membership, too costly to repeat for
    // every connection of every frame; a short-lived memo keeps one render pass to one lookup.
    private WaveguideCompanionSpec DesignWide()
    {
        long now = Environment.TickCount64;
        if (_designWideMemo is { } memo && now - _designWideMemoAt < DesignWideMemoMilliseconds)
            return memo;
        _designWideMemo = _designWide();
        _designWideMemoAt = now;
        return _designWideMemo;
    }

    private string? PdkOf(Component? component, IReadOnlyCollection<ComponentTemplate> templates)
    {
        if (component == null)
            return null;
        var key = !string.IsNullOrEmpty(component.GdsFactoryFunction)
            ? "gf:" + component.GdsFactoryFunction
            : string.IsNullOrEmpty(component.NazcaFunctionName) ? null : "nazca:" + component.NazcaFunctionName;
        if (key == null)
            return null;
        return _pdkByFunction.GetOrAdd(key, _ => ComponentPdkSourceResolver.Resolve(component, templates));
    }
}
