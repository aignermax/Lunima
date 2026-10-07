using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;

/// <summary>
/// A freshly instantiated PDK crossing component together with the template
/// metadata the canvas needs to create its <see cref="ComponentViewModel"/>
/// (so persistence and export treat it like a normally placed PDK component).
/// </summary>
/// <param name="Component">The crossing component instance (e.g. ebeam_crossing4).</param>
/// <param name="TemplateName">Display name of the source PDK template (e.g. "Crossing 4-Port").</param>
/// <param name="TemplatePdkSource">Name of the PDK the template comes from (e.g. "SiEPIC EBeam").</param>
public record CrossingComponentInstance(
    Component Component,
    string? TemplateName,
    string? TemplatePdkSource)
{
    /// <summary>Nazca function name of the SiEPIC crossing — the default when nothing else is preferred.</summary>
    public const string CrossingNazcaFunctionName = "ebeam_crossing4";

    /// <summary>Nazca function name of the Demo PDK crossing (two crossing demofab straights).</summary>
    public const string DemoCrossingNazcaFunctionName = "demo_crossing";

    /// <summary>Known 4-port crossing components, in default priority order.</summary>
    private static readonly string[] KnownCrossingFunctions = { CrossingNazcaFunctionName, DemoCrossingNazcaFunctionName };

    /// <summary>True when <paramref name="template"/> is one of the known 4-port crossings.</summary>
    public static bool IsCrossingTemplate(ComponentTemplate template) =>
        KnownCrossingFunctions.Contains(template.NazcaFunctionName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Finds the loaded crossing template to insert, or null while none is available.
    /// A crossing from one of <paramref name="preferredPdks"/> (in order) wins — so a
    /// demofab design gets the Demo crossing and stays within its process; otherwise
    /// the SiEPIC crossing, then any known crossing.
    /// </summary>
    /// <param name="templates">The loaded component library.</param>
    /// <param name="preferredPdks">PDK names to prefer, most preferred first (e.g. the PDKs on the canvas).</param>
    public static ComponentTemplate? FindCrossingTemplate(
        IEnumerable<ComponentTemplate> templates, IEnumerable<string>? preferredPdks = null)
    {
        var crossings = templates.Where(IsCrossingTemplate).ToList();
        foreach (var pdk in preferredPdks ?? Enumerable.Empty<string>())
        {
            var match = crossings.FirstOrDefault(t => string.Equals(t.PdkSource, pdk, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return KnownCrossingFunctions
            .Select(function => crossings.FirstOrDefault(t =>
                string.Equals(t.NazcaFunctionName, function, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(t => t != null);
    }

    /// <summary>
    /// The PDKs of the placed components (group contents included), most frequent first —
    /// the order <see cref="FindCrossingTemplate"/> prefers, so a crossing joins the PDK
    /// the design is built from.
    /// </summary>
    /// <param name="components">The top-level canvas components.</param>
    /// <param name="library">The loaded component library, to resolve each component's PDK.</param>
    public static IReadOnlyList<string> PreferredPdksOf(
        IEnumerable<Component> components, IEnumerable<ComponentTemplate> library)
    {
        var templates = library.ToList();
        return components
            .SelectMany(c => c is ComponentGroup group ? group.GetAllComponentsRecursive() : (IEnumerable<Component>)new[] { c })
            .Where(c => c is not ComponentGroup)
            .Select(c => ComponentPdkSourceResolver.Resolve(c, templates))
            .OfType<string>()
            .GroupBy(pdk => pdk, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToList();
    }

    /// <summary>
    /// Instantiates a fresh crossing component through the production PDK path
    /// (PDK JSON → <see cref="ComponentTemplate"/> → <see cref="ComponentTemplates.CreateFromTemplate"/>).
    /// Returns null while no crossing template is loaded.
    /// </summary>
    /// <param name="templates">The loaded component library.</param>
    /// <param name="preferredPdks">PDK names to prefer, most preferred first.</param>
    public static CrossingComponentInstance? CreateFromTemplates(
        IEnumerable<ComponentTemplate> templates, IEnumerable<string>? preferredPdks = null)
    {
        var template = FindCrossingTemplate(templates, preferredPdks);
        if (template == null) return null;

        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);
        return new CrossingComponentInstance(component, template.Name, template.PdkSource);
    }
}
