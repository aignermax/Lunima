using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Components.ComponentHelpers;

/// <summary>
/// Helper methods for filtering waveguide connections based on component hierarchy.
/// Used to prevent rendering duplicate connections when components are grouped.
/// </summary>
public static class WaveguideFilteringHelper
{
    /// <summary>
    /// Checks if a waveguide connection is internal to a ComponentGroup.
    /// A connection is internal if both its start and end pins belong to components
    /// that are direct children of the same group.
    /// </summary>
    /// <param name="connection">The waveguide connection to check.</param>
    /// <param name="allGroups">All ComponentGroups currently on the canvas.</param>
    /// <returns>True if the connection is internal to a group, false otherwise.</returns>
    public static bool IsConnectionInternalToAnyGroup(
        WaveguideConnection connection,
        IEnumerable<ComponentGroup> allGroups)
    {
        if (connection == null)
            return false;

        var startComponent = connection.StartPin.ParentComponent;
        var endComponent = connection.EndPin.ParentComponent;

        // Check each group to see if both components are children
        foreach (var group in allGroups)
        {
            if (IsConnectionInternalToGroup(connection, group))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a waveguide connection is internal to a specific ComponentGroup.
    /// A connection is internal if both its start and end pins belong to components
    /// that are direct children of the given group.
    /// </summary>
    /// <param name="connection">The waveguide connection to check.</param>
    /// <param name="group">The ComponentGroup to check against.</param>
    /// <returns>True if the connection is internal to the group, false otherwise.</returns>
    public static bool IsConnectionInternalToGroup(
        WaveguideConnection connection,
        ComponentGroup group)
    {
        if (connection == null || group == null)
            return false;

        var startComponent = connection.StartPin.ParentComponent;
        var endComponent = connection.EndPin.ParentComponent;

        // Connection is internal if both components are direct children of this group
        bool startIsChild = group.ChildComponents.Contains(startComponent);
        bool endIsChild = group.ChildComponents.Contains(endComponent);

        return startIsChild && endIsChild;
    }

    /// <summary>
    /// Recursively collects all ComponentGroups from a list of components.
    /// Includes nested groups.
    /// </summary>
    /// <param name="components">The components to search through.</param>
    /// <returns>A list of all ComponentGroups found.</returns>
    public static List<ComponentGroup> CollectAllGroups(IEnumerable<Component> components)
    {
        var groups = new List<ComponentGroup>();

        foreach (var component in components)
        {
            if (component is ComponentGroup group)
            {
                groups.Add(group);
                // Recursively collect nested groups
                groups.AddRange(CollectAllGroups(group.ChildComponents));
            }
        }

        return groups;
    }
}
