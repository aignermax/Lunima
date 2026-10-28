using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;

namespace CAP_DataAccess.Persistence.NativeDesign;

/// <summary>Orders native group records by their child dependencies.</summary>
public static class NativeGroupOrder
{
    /// <summary>
    /// Sorts groups in topological order so that child groups are loaded before their parents.
    /// This ensures that when we reconstruct a parent group, all its child groups are already available.
    /// </summary>
    public static List<DesignGroupData> TopologicalSortGroups(List<DesignGroupData> groupDataList)
    {
        // Dependency bookkeeping keys are the data objects themselves, not the group
        // identifier: identifiers are not unique — two groups sharing one must never
        // merge into two copies of the first on load.
        var dependents = new Dictionary<DesignGroupData, List<DesignGroupData>>();
        var inDegree = new Dictionary<DesignGroupData, int>();

        foreach (var groupData in groupDataList)
        {
            if (!inDegree.ContainsKey(groupData))
                inDegree[groupData] = 0;

            // Count how many child groups this group has (determines loading order)
            var childIds = groupData.GroupDto.ChildComponentIds;
            var childGuids = groupData.GroupDto.ChildComponentGuids;
            for (var i = 0; i < childIds.Count; i++)
            {
                // Check if this child is a group (appears as a group in the list).
                // Match by saved Guid: identifiers are not unique, so a string match
                // can pick a same-named group that merely appears earlier in the file
                // and misorder the reconstruction (the parent then binds the wrong
                // child through the name fallback). The identifier match remains as
                // the fallback for files that predate the Guid fields.
                DesignGroupData? childGroup = null;
                if (i < childGuids.Count && Guid.TryParse(childGuids[i], out var childGuid))
                {
                    childGroup = groupDataList.FirstOrDefault(g =>
                        g.GroupDto.IdGuid != null
                        && Guid.TryParse(g.GroupDto.IdGuid, out var groupGuid)
                        && groupGuid == childGuid);
                }

                childGroup ??= groupDataList.FirstOrDefault(g => g.GroupDto.Identifier == childIds[i]);
                if (childGroup != null && childGroup != groupData)
                {
                    // This group depends on its child group being loaded first
                    if (!dependents.ContainsKey(childGroup))
                        dependents[childGroup] = new List<DesignGroupData>();
                    dependents[childGroup].Add(groupData);
                    inDegree[groupData]++;
                }
            }
        }

        // Kahn's algorithm for topological sort
        var queue = new Queue<DesignGroupData>();
        foreach (var groupData in groupDataList)
        {
            if (inDegree[groupData] == 0)
                queue.Enqueue(groupData);
        }

        var sorted = new List<DesignGroupData>();
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            sorted.Add(current);

            if (dependents.TryGetValue(current, out var dependentList))
            {
                foreach (var dependent in dependentList)
                {
                    inDegree[dependent]--;
                    if (inDegree[dependent] == 0)
                        queue.Enqueue(dependent);
                }
            }
        }

        // If we couldn't sort all groups, there's a cycle (shouldn't happen)
        // Just return the original order as fallback
        return sorted.Count == groupDataList.Count ? sorted : groupDataList;
    }
}
