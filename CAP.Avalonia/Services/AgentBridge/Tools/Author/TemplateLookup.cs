using CAP.Avalonia.ViewModels.Library;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>Resolves a component type name (optionally narrowed by PDK) to one library template.</summary>
public static class TemplateLookup
{
    /// <summary>A template by Lunima name, else by its Nazca or gdsfactory function (netlist imports).</summary>
    public static ComponentTemplate? ResolveAny(IReadOnlyList<ComponentTemplate> templates, string type, string? pdk = null)
    {
        var candidates = templates.Where(t => pdk == null || string.Equals(t.PdkSource, pdk, StringComparison.OrdinalIgnoreCase)).ToList();
        return candidates.FirstOrDefault(t => string.Equals(t.Name, type, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(t => string.Equals(t.NazcaFunctionName, type, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(t => string.Equals(t.GdsFactoryFunction, type, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The template called <paramref name="type"/> (case-insensitive), restricted to
    /// <paramref name="pdk"/> when given. Returns null with a reason when nothing or more than
    /// one template matches.
    /// </summary>
    public static ComponentTemplate? Find(
        IReadOnlyList<ComponentTemplate> templates, string? type, string? pdk, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(type))
        {
            error = "Pass the component type (a name from list_component_types).";
            return null;
        }

        var matches = templates
            .Where(t => string.Equals(t.Name, type, StringComparison.OrdinalIgnoreCase))
            .Where(t => pdk == null || string.Equals(t.PdkSource, pdk, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 1)
            return matches[0];

        error = matches.Count == 0
            ? $"No component type '{type}'{(pdk != null ? $" in PDK '{pdk}'" : "")}. Use list_component_types."
            : $"'{type}' exists in several PDKs ({string.Join(", ", matches.Select(m => m.PdkSource).Distinct())}); pass pdk.";
        return null;
    }
}
