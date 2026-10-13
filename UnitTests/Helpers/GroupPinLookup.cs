using System.Text.RegularExpressions;
using CAP_Core.Components.Core;

namespace UnitTests.Helpers;

/// <summary>
/// Finds a group's external pin in journey tests that name pins by the component pin
/// they expose. Groups created with Ctrl+G name their external pins by direction
/// (IN1, OUT1, E1… — see <c>GroupPinNamer</c>), so a test that means "the pin exposing
/// a_ec.fiber" asks for <c>"a_ec_fiber"</c> and this helper resolves it by the exact
/// external name (loaded examples, renamed gate pins such as A or Y), by the
/// <c>&lt;componentId&gt;_&lt;pinName&gt;</c> of the exposed component pin, or — for
/// placed prefab instances, whose children carry a <c>_&lt;guid&gt;</c> clone suffix —
/// by that id without the suffix.
/// </summary>
internal static class GroupPinLookup
{
    private static readonly Regex CloneSuffix = new("_[0-9a-f]{32}$", RegexOptions.Compiled);

    /// <summary>Returns the external pin named <paramref name="name"/> or exposing that component pin.</summary>
    /// <param name="group">The group whose external pins are searched.</param>
    /// <param name="name">External pin name, or <c>&lt;componentId&gt;_&lt;pinName&gt;</c> of the exposed pin.</param>
    public static GroupPin Find(ComponentGroup group, string name) =>
        group.ExternalPins.FirstOrDefault(p => p.Name == name)
        ?? group.ExternalPins.FirstOrDefault(p => ExposedId(p) == name)
        ?? group.ExternalPins.Single(p => TemplateExposedId(p) == name);

    /// <summary>The <c>&lt;componentId&gt;_&lt;pinName&gt;</c> id of the component pin an external pin exposes.</summary>
    public static string ExposedId(GroupPin pin) =>
        $"{pin.InternalPin.ParentComponent.Identifier}_{pin.InternalPin.Name}";

    /// <summary>The exposed id with a prefab instance's clone suffix removed from the component id.</summary>
    private static string TemplateExposedId(GroupPin pin) =>
        $"{CloneSuffix.Replace(pin.InternalPin.ParentComponent.Identifier, "")}_{pin.InternalPin.Name}";
}
