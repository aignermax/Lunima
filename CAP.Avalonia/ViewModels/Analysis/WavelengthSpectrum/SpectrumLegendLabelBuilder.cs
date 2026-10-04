using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;

/// <summary>
/// Builds the unique, human-readable curve labels for the spectrum legend.
/// Pins are labelled "Component.pin"; when several components share
/// one display name (e.g. four "Grating Coupler TE 1550" instances), the
/// type-named ones get a stable " #n" suffix so every legend entry is
/// distinguishable. A user-set <see cref="Component.HumanReadableName"/>
/// always wins and is shown verbatim.
/// </summary>
internal static class SpectrumLegendLabelBuilder
{
    /// <summary>
    /// Resolves a unique display name per component. Components whose
    /// <see cref="Component.HumanReadableName"/> is set keep it as-is;
    /// type-named components that collide with a sibling get a stable
    /// 1-based " #n" suffix in enumeration order.
    /// </summary>
    /// <param name="components">All canvas components (groups flattened).</param>
    public static IReadOnlyDictionary<Component, string> BuildDisplayNames(
        IReadOnlyList<Component> components)
    {
        var result = new Dictionary<Component, string>(components.Count);
        var groups = components.GroupBy(DisplayNameOf);
        foreach (var group in groups)
        {
            var members = group.ToList();
            if (members.Count == 1)
            {
                result[members[0]] = group.Key;
                continue;
            }
            for (int i = 0; i < members.Count; i++)
            {
                result[members[i]] = HasHumanName(members[i])
                    ? group.Key
                    : $"{group.Key} #{i + 1}";
            }
        }
        return result;
    }

    /// <summary>
    /// Maps every light-pin flow id to its "Component.pin" label, using the
    /// unique display names from <see cref="BuildDisplayNames"/>.
    /// </summary>
    /// <param name="displayNames">Unique per-component display names.</param>
    public static Dictionary<Guid, string> BuildPinNameMap(
        IReadOnlyDictionary<Component, string> displayNames)
    {
        var map = new Dictionary<Guid, string>();
        foreach (var (component, displayName) in displayNames)
        {
            foreach (var pin in component.PhysicalPins)
            {
                if (pin.LogicalPin == null) continue;
                var pinLabel = $"{displayName}.{pin.Name}";
                map[pin.LogicalPin.IDInFlow] = pinLabel;
                map[pin.LogicalPin.IDOutFlow] = pinLabel;
            }
        }
        return map;
    }

    /// <summary>
    /// Names both ends of the measured path when the sweep has a single
    /// light source ("In → Drop.port 2"); returns the output label unchanged
    /// when no — or several — inputs are active (a superposed curve cannot
    /// be attributed to one input).
    /// </summary>
    /// <param name="inputLabel">Display name of the single active source, or null.</param>
    /// <param name="outputLabel">Label of the plotted output pin.</param>
    public static string ComposeCurveLabel(string? inputLabel, string outputLabel) =>
        string.IsNullOrEmpty(inputLabel) ? outputLabel : $"{inputLabel} → {outputLabel}";

    private static string DisplayNameOf(Component component) =>
        HasHumanName(component) ? component.HumanReadableName! : component.Name;

    private static bool HasHumanName(Component component) =>
        !string.IsNullOrEmpty(component.HumanReadableName);
}
