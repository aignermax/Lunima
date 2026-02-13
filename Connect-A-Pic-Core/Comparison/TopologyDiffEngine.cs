namespace CAP_Core.Comparison;

/// <summary>
/// Compares the topology of two design snapshots and produces a list of differences.
/// Matching is based on component template name and identifier.
/// </summary>
public static class TopologyDiffEngine
{
    /// <summary>
    /// Computes the topology differences between two designs.
    /// </summary>
    public static IReadOnlyList<TopologyDifference> Compare(
        DesignSnapshot a,
        DesignSnapshot b)
    {
        var differences = new List<TopologyDifference>();

        CompareComponents(a, b, differences);
        CompareConnections(a, b, differences);

        return differences;
    }

    private static void CompareComponents(
        DesignSnapshot a,
        DesignSnapshot b,
        List<TopologyDifference> differences)
    {
        var aByType = GroupByTemplate(a.Components);
        var bByType = GroupByTemplate(b.Components);

        var allTypes = aByType.Keys.Union(bByType.Keys);

        foreach (var type in allTypes)
        {
            int countA = aByType.GetValueOrDefault(type, 0);
            int countB = bByType.GetValueOrDefault(type, 0);

            if (countA > 0 && countB == 0)
            {
                differences.Add(new TopologyDifference(
                    DifferenceKind.OnlyInA, type,
                    $"{countA}x {type} only in Design A"));
            }
            else if (countB > 0 && countA == 0)
            {
                differences.Add(new TopologyDifference(
                    DifferenceKind.OnlyInB, type,
                    $"{countB}x {type} only in Design B"));
            }
            else if (countA != countB)
            {
                differences.Add(new TopologyDifference(
                    DifferenceKind.Modified, type,
                    $"{type}: {countA} in A vs {countB} in B"));
            }
        }
    }

    private static void CompareConnections(
        DesignSnapshot a,
        DesignSnapshot b,
        List<TopologyDifference> differences)
    {
        var aKeys = BuildConnectionKeys(a);
        var bKeys = BuildConnectionKeys(b);

        foreach (var key in aKeys.Except(bKeys))
        {
            differences.Add(new TopologyDifference(
                DifferenceKind.ConnectionOnlyInA, key,
                $"Connection {key} only in Design A"));
        }

        foreach (var key in bKeys.Except(aKeys))
        {
            differences.Add(new TopologyDifference(
                DifferenceKind.ConnectionOnlyInB, key,
                $"Connection {key} only in Design B"));
        }
    }

    private static Dictionary<string, int> GroupByTemplate(
        IReadOnlyList<SnapshotComponent> components)
    {
        return components
            .GroupBy(c => c.TemplateName)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// Builds canonical connection keys using component template names and pin names.
    /// This enables structural comparison regardless of component ordering.
    /// </summary>
    private static HashSet<string> BuildConnectionKeys(DesignSnapshot snapshot)
    {
        var keys = new HashSet<string>();

        foreach (var conn in snapshot.Connections)
        {
            if (conn.StartComponentIndex < 0 ||
                conn.StartComponentIndex >= snapshot.Components.Count ||
                conn.EndComponentIndex < 0 ||
                conn.EndComponentIndex >= snapshot.Components.Count)
            {
                continue;
            }

            var startComp = snapshot.Components[conn.StartComponentIndex];
            var endComp = snapshot.Components[conn.EndComponentIndex];

            // Build a normalized key (sorted so A->B == B->A)
            var a = $"{startComp.TemplateName}.{conn.StartPinName}";
            var b = $"{endComp.TemplateName}.{conn.EndPinName}";

            var key = string.Compare(a, b, StringComparison.Ordinal) <= 0
                ? $"{a} <-> {b}"
                : $"{b} <-> {a}";

            keys.Add(key);
        }

        return keys;
    }
}
