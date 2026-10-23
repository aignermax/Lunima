using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels.Panels.DesignModels;

/// <summary>Resolves persisted template identity against placed instances and the current library.</summary>
public sealed class ComponentTemplateResolver(
    IEnumerable<ComponentViewModel> components, IEnumerable<ComponentTemplate> library)
{
    /// <summary>Finds a saved template, preferring its PDK before the legacy name-only fallback.</summary>
    public ComponentTemplate? Find(string templateName, string? pdkSource)
    {
        if (!string.IsNullOrEmpty(pdkSource))
        {
            var exact = library.FirstOrDefault(t =>
                t.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase)
                && t.PdkSource.Equals(pdkSource, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }
        return library.FirstOrDefault(t => t.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Gets the instance's template name, preserving explicit stamps on grouped children.</summary>
    public string Name(Component component)
    {
        var vm = components.FirstOrDefault(c => c.Component == component);
        if (vm?.TemplateName != null) return vm.TemplateName;
        if (!string.IsNullOrEmpty(component.TemplateName) && library.Any(t => t.Name == component.TemplateName))
            return component.TemplateName;
        var nazcaFunc = component.NazcaFunctionName;
        if (!string.IsNullOrEmpty(nazcaFunc))
        {
            var match = library.FirstOrDefault(t =>
                (t.NazcaFunctionName ?? $"nazca_{t.Name.ToLower().Replace(" ", "_")}") == nazcaFunc);
            if (match != null) return match.Name;
        }
        return component.Identifier;
    }

    /// <summary>Resolves the originating PDK using the shared component identity policy.</summary>
    public string? PdkSource(Component component) => ComponentPdkSourceResolver.Resolve(component, library);

    /// <summary>Returns the PDK-qualified key used by model overrides.</summary>
    public string? Key(Component component)
    {
        var pdk = PdkSource(component);
        return pdk == null ? null : $"{pdk}::{Name(component)}";
    }

    /// <summary>Distinguishes deferred overrides from keys with no matching loaded template.</summary>
    public bool ContainsKey(string key)
    {
        var separator = key.IndexOf("::", StringComparison.Ordinal);
        if (separator < 0) return false;
        var pdk = key[..separator];
        var name = key[(separator + 2)..];
        return library.Any(t => t.PdkSource == pdk && t.Name == name);
    }
}
