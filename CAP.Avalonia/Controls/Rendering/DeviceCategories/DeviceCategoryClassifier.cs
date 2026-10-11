using System.Runtime.CompilerServices;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls.Rendering.DeviceCategories;

/// <summary>
/// Infers a <see cref="DeviceCategory"/> from what a placed component carries — its template
/// name, display name and Nazca function — plus its pins (an electrically driven device is
/// active). Placed components do not keep the library category, and loaded designs only store
/// these names, so the classification is keyword based and cached per component instance.
/// </summary>
public static class DeviceCategoryClassifier
{
    private static readonly ConditionalWeakTable<Component, CachedCategory> Cache = new();

    /// <summary>Ordered rules: the first rule with a matching keyword wins, so the more specific
    /// classes (a grating "coupler" is I/O, a "disconnected waveguide" is a terminator) come first.</summary>
    private static readonly (DeviceCategory Category, string[] Substrings, string[] Tokens)[] Rules =
    {
        (DeviceCategory.Detector, new[] { "detector", "photodiode", "terminat", "disconnected", "absorber", "analyzer" }, new[] { "pd" }),
        (DeviceCategory.Electrical, new[] { "bondpad", "bond pad", "probe pad", "pad_dc" }, new[] { "pad" }),
        (DeviceCategory.InputOutput, new[] { "grating", "edge coupler", "edge_coupler", "fiber", "laser" }, new[] { "gc", "io" }),
        (DeviceCategory.Active, new[] { "phase", "modulat", "heater", "shifter", "eopm", "thermo" }, new[] { "eam", "eom", "ps" }),
        (DeviceCategory.Filter, new[] { "contra", "filter", "bragg", "ring", "mzi", "interferomet", "polariz", "awg", "resonat" }, new[] { "dbr" }),
        (DeviceCategory.Crossing, new[] { "cross" }, Array.Empty<string>()),
        (DeviceCategory.Coupler, new[] { "mmi", "coupler", "splitter", "junction", "branch", "combiner", "adiabatic" }, new[] { "dc", "bdc", "y" }),
        (DeviceCategory.Waveguide, new[] { "waveguide", "straight", "strt", "bend", "taper", "spiral", "delay", "euler" }, new[] { "wg" }),
    };

    /// <summary>The category of <paramref name="component"/>, cached until its names change.</summary>
    public static DeviceCategory Classify(Component component)
    {
        var cached = Cache.GetOrCreateValue(component);
        string? name = component.HumanReadableName;
        if (cached.HasValue && ReferenceEquals(cached.Name, name))
            return cached.Category;

        cached.Category = ClassifyNames(component.TemplateName, name, component.NazcaFunctionName,
            HasElectricalPin(component));
        cached.Name = name;
        cached.HasValue = true;
        return cached.Category;
    }

    /// <summary>Pure classification from names and pin domain (exposed for tests).</summary>
    public static DeviceCategory ClassifyNames(string? templateName, string? displayName, string? nazcaFunction,
        bool hasElectricalPin)
    {
        string text = $"{templateName} {displayName} {nazcaFunction}".ToLowerInvariant();
        var tokens = text.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var (category, substrings, ruleTokens) in Rules)
        {
            if (substrings.Any(text.Contains) || ruleTokens.Any(tokens.Contains))
                return category;
        }
        return hasElectricalPin ? DeviceCategory.Active : DeviceCategory.Other;
    }

    private static readonly char[] TokenSeparators = { ' ', '_', '-', '.', '(', ')', '/', ',' };

    private static bool HasElectricalPin(Component component)
    {
        foreach (var pin in component.PhysicalPins)
        {
            if (pin.MatterType == MatterType.Electricity)
                return true;
        }
        return false;
    }

    private sealed class CachedCategory
    {
        public bool HasValue;
        public string? Name;
        public DeviceCategory Category;
    }
}
