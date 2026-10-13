using System.Globalization;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;

namespace CAP_Core.Analysis;

/// <summary>
/// Design checks of a multi-die assembly: dies must not overlap, every part must sit on its
/// die (none on the bare carrier), and every edge coupler's facet must lie on its die's
/// edge — light leaves a die only through a facet on its edge. A single-die design (no die
/// groups) produces no findings; its chip bounds are checked by
/// <see cref="DesignValidator.ValidateComponentBounds"/> as before.
/// </summary>
public class DieAssemblyChecker
{
    /// <summary>A facet within this distance of the die edge counts as on the edge.</summary>
    public const double FacetEdgeToleranceMicrometers = ChipletInterfaceChecker.EdgeToleranceMicrometers;

    private const double OutlineToleranceMicrometers = 1e-6;

    /// <summary>Checks the assembly formed by <paramref name="topLevelComponents"/>.</summary>
    public List<DesignIssue> Check(IEnumerable<Component> topLevelComponents)
    {
        ArgumentNullException.ThrowIfNull(topLevelComponents);
        var topLevel = topLevelComponents.ToList();
        var dies = DieAssembly.DiesOf(topLevel);
        var issues = new List<DesignIssue>();
        if (dies.Count == 0)
            return issues;

        CheckOverlaps(dies, issues);
        foreach (var loose in topLevel.Where(c => c is not ComponentGroup { IsDie: true } && c.IsRoutingObstacle))
            issues.Add(OffDie(loose, null));
        foreach (var die in dies)
            CheckDieContent(die, issues);
        return issues;
    }

    private static void CheckOverlaps(IReadOnlyList<ComponentGroup> dies, List<DesignIssue> issues)
    {
        for (int i = 0; i < dies.Count; i++)
        {
            for (int j = i + 1; j < dies.Count; j++)
            {
                var a = DieAssembly.OutlineOf(dies[i]);
                var b = DieAssembly.OutlineOf(dies[j]);
                if (!a.Overlaps(b))
                    continue;
                string nameA = dies[i].GroupName, nameB = dies[j].GroupName;
                issues.Add(new DesignIssue(
                    DesignIssueType.DieOverlap, null,
                    (Math.Max(a.X, b.X) + Math.Min(a.Right, b.Right)) / 2,
                    (Math.Max(a.Y, b.Y) + Math.Min(a.Bottom, b.Bottom)) / 2,
                    $"Dies '{nameA}' and '{nameB}' overlap",
                    localizationKey: "DesignChecks.DieOverlap",
                    localizationArgs: new object[] { nameA, nameB }));
            }
        }
    }

    private static void CheckDieContent(ComponentGroup die, List<DesignIssue> issues)
    {
        var outline = DieAssembly.OutlineOf(die);
        foreach (var child in die.ChildComponents)
        {
            var (x, y, w, h) = FootprintOf(child);
            if (child.IsRoutingObstacle && !outline.ContainsRect(x, y, w, h, OutlineToleranceMicrometers))
                issues.Add(OffDie(child, die));
        }

        foreach (var coupler in die.GetAllComponentsRecursive().Where(EdgeCouplerFacet.IsEdgeCoupler))
        {
            double distance = DieEdgeSnapper.FacetDistanceToEdge(coupler, outline);
            if (distance <= FacetEdgeToleranceMicrometers)
                continue;
            var (fx, fy) = EdgeCouplerFacet.FacetPinOf(coupler)!.GetAbsolutePosition();
            string name = NameOf(coupler);
            issues.Add(new DesignIssue(
                DesignIssueType.EdgeCouplerOffDieEdge, null, fx, fy,
                string.Create(CultureInfo.InvariantCulture,
                    $"Edge coupler '{name}' on '{die.GroupName}' has its facet {distance:F1} µm from the die edge"),
                localizationKey: "DesignChecks.EdgeCouplerOffDieEdge",
                localizationArgs: new object[] { name, die.GroupName, distance }));
        }
    }

    private static DesignIssue OffDie(Component component, ComponentGroup? die)
    {
        var (x, y, w, h) = FootprintOf(component);
        string name = NameOf(component);
        return die == null
            ? new DesignIssue(DesignIssueType.ComponentOffDie, null, x + w / 2, y + h / 2,
                $"'{name}' is not on any die",
                localizationKey: "DesignChecks.ComponentNotOnDie",
                localizationArgs: new object[] { name })
            : new DesignIssue(DesignIssueType.ComponentOffDie, null, x + w / 2, y + h / 2,
                $"'{name}' sticks out of die '{die.GroupName}'",
                localizationKey: "DesignChecks.ComponentOutsideDie",
                localizationArgs: new object[] { name, die.GroupName });
    }

    private static (double X, double Y, double W, double H) FootprintOf(Component component) =>
        component is ComponentGroup g
            ? (g.PhysicalX + g.MinChildOffsetX, g.PhysicalY + g.MinChildOffsetY, g.WidthMicrometers, g.HeightMicrometers)
            : (component.PhysicalX, component.PhysicalY, component.WidthMicrometers, component.HeightMicrometers);

    private static string NameOf(Component component) =>
        component is ComponentGroup g ? g.GroupName : component.HumanReadableName ?? component.Identifier;
}
