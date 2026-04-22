using System.Text;
using CAP_Core.Components.Core;

namespace CAP_Core.Export;

/// <summary>
/// Maps <see cref="Component.Identifier"/> values to unique, Python-safe variable
/// names for PhotonTorch export. Prefix comes from the component's
/// <see cref="Component.NazcaFunctionName"/>.
/// </summary>
internal sealed class ComponentNameMap
{
    private readonly Dictionary<string, string> _map;

    private ComponentNameMap(Dictionary<string, string> map) => _map = map;

    internal string this[string componentId] => _map[componentId];

    internal bool TryGet(string componentId, out string name) => _map.TryGetValue(componentId, out name!);

    internal static ComponentNameMap Build(IReadOnlyList<Component> components)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var comp in components)
        {
            var prefix = GetComponentPrefix(comp.NazcaFunctionName);
            var candidate = $"{prefix}_{Sanitize(comp.Identifier)}";
            map[comp.Identifier] = EnsureUnique(candidate, used);
        }

        return new ComponentNameMap(map);
    }

    private static string GetComponentPrefix(string? nazcaFunctionName)
    {
        var name = (nazcaFunctionName ?? "").ToLowerInvariant();
        if (name.Contains("wg") || name.Contains("straight") || name.Contains("waveguide")) return "wg";
        if (name.Contains("dc") || name.Contains("directional")) return "dc";
        if (name.Contains("mmi") || name.Contains("splitter")) return "mmi";
        if (name.Contains("gc") || name.Contains("grating")) return "gc";
        if (name.Contains("phase") || name.Contains("ps")) return "ps";
        return "comp";
    }

    private static string Sanitize(string identifier)
    {
        var sb = new StringBuilder();
        foreach (var c in identifier)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.Length > 0 ? sb.ToString() : "x";
    }

    private static string EnsureUnique(string candidate, HashSet<string> used)
    {
        if (used.Add(candidate)) return candidate;
        for (int i = 2; ; i++)
        {
            var numbered = $"{candidate}_{i}";
            if (used.Add(numbered)) return numbered;
        }
    }
}

/// <summary>
/// Terminal (<c>pt.Source</c> or <c>pt.Detector</c>) allocated for an unconnected pin.
/// </summary>
internal readonly record struct Termination(string VarName, bool IsSource, string ComponentVar, int PortIndex);
