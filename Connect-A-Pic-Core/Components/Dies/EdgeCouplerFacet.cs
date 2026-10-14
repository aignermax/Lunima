using System;
using System.Linq;
using CAP_Core.Components.Core;

namespace CAP_Core.Components.Dies;

/// <summary>
/// Identifies edge couplers and their facet pin. An edge coupler is recognised by its
/// template stamp containing "edge coupler" — the naming every bundled PDK follows and the
/// same rule the chiplet-interface check uses. Its facet is the pin that faces off the die:
/// the pin named "fiber"/"facet" when present, else its first pin.
/// </summary>
public static class EdgeCouplerFacet
{
    private const string EdgeCouplerTemplateToken = "edge coupler";
    private static readonly string[] FacetPinTokens = { "fiber", "facet" };

    /// <summary>True when <paramref name="component"/> is an edge coupler.</summary>
    public static bool IsEdgeCoupler(Component component) =>
        component is not ComponentGroup
        && component.TemplateName?.Contains(EdgeCouplerTemplateToken, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The facet pin of an edge coupler, or null when the component has no pins.</summary>
    public static PhysicalPin? FacetPinOf(Component component) =>
        component.PhysicalPins.FirstOrDefault(p =>
            FacetPinTokens.Any(t => p.Name.Contains(t, StringComparison.OrdinalIgnoreCase)))
        ?? component.PhysicalPins.FirstOrDefault();
}
