using CAP_Core.Components.Core;

namespace CAP_Core.Components.ComponentHelpers;

/// <summary>
/// Provides rendering logic for ComponentGroup instances.
/// Calculates bounding boxes and provides data for visual representation.
/// </summary>
public class ComponentGroupRenderer
{
    /// <summary>
    /// Calculates the bounding box for a group instance.
    /// </summary>
    /// <param name="groupInstance">The group instance to calculate bounds for.</param>
    /// <returns>Tuple containing (minX, minY, maxX, maxY) in micrometers.</returns>
    public static (double minX, double minY, double maxX, double maxY) CalculateGroupBounds(
        ComponentGroupInstance groupInstance)
    {
        if (groupInstance == null || groupInstance.Components.Count == 0)
            return (0, 0, 0, 0);

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;

        foreach (var component in groupInstance.Components)
        {
            double compMinX = component.PhysicalX;
            double compMinY = component.PhysicalY;
            double compMaxX = component.PhysicalX + component.WidthMicrometers;
            double compMaxY = component.PhysicalY + component.HeightMicrometers;

            minX = Math.Min(minX, compMinX);
            minY = Math.Min(minY, compMinY);
            maxX = Math.Max(maxX, compMaxX);
            maxY = Math.Max(maxY, compMaxY);
        }

        return (minX, minY, maxX, maxY);
    }

    /// <summary>
    /// Calculates the padded bounding box for rendering the group border.
    /// </summary>
    /// <param name="groupInstance">The group instance.</param>
    /// <param name="paddingMicrometers">Padding around components in micrometers.</param>
    /// <returns>Tuple containing (minX, minY, width, height) for rendering.</returns>
    public static (double x, double y, double width, double height) CalculatePaddedBounds(
        ComponentGroupInstance groupInstance,
        double paddingMicrometers = 10.0)
    {
        var (minX, minY, maxX, maxY) = CalculateGroupBounds(groupInstance);

        return (
            minX - paddingMicrometers,
            minY - paddingMicrometers,
            maxX - minX + 2 * paddingMicrometers,
            maxY - minY + 2 * paddingMicrometers
        );
    }

    /// <summary>
    /// Checks if a point is inside the group's bounding box.
    /// </summary>
    /// <param name="groupInstance">The group instance.</param>
    /// <param name="x">X coordinate in micrometers.</param>
    /// <param name="y">Y coordinate in micrometers.</param>
    /// <param name="paddingMicrometers">Padding to include in hit test.</param>
    /// <returns>True if point is inside the group bounds.</returns>
    public static bool HitTestGroupBounds(
        ComponentGroupInstance groupInstance,
        double x,
        double y,
        double paddingMicrometers = 10.0)
    {
        var (boundsX, boundsY, width, height) = CalculatePaddedBounds(groupInstance, paddingMicrometers);

        return x >= boundsX &&
               x <= boundsX + width &&
               y >= boundsY &&
               y <= boundsY + height;
    }

    /// <summary>
    /// Gets the top-level group instance for a component.
    /// Traverses up the hierarchy if there are nested groups.
    /// </summary>
    /// <param name="component">The component to find the top-level group for.</param>
    /// <param name="allGroupInstances">Dictionary of all group instances.</param>
    /// <returns>The top-level group instance, or null if component is not in a group.</returns>
    public static ComponentGroupInstance? GetTopLevelGroup(
        Component component,
        Dictionary<Guid, ComponentGroupInstance> allGroupInstances)
    {
        if (component.ParentGroupInstanceId == null)
            return null;

        var groupInstance = allGroupInstances.GetValueOrDefault(component.ParentGroupInstanceId.Value);

        // For now, we don't support nested groups, so just return the group
        // In the future, this could traverse up the hierarchy
        return groupInstance;
    }

    /// <summary>
    /// Determines if a component should be highlighted as part of a group hover.
    /// </summary>
    /// <param name="component">The component to check.</param>
    /// <param name="hoveredGroupInstance">The currently hovered group instance.</param>
    /// <returns>True if the component should be highlighted.</returns>
    public static bool ShouldHighlightAsGroupMember(
        Component component,
        ComponentGroupInstance? hoveredGroupInstance)
    {
        if (hoveredGroupInstance == null)
            return false;

        return component.ParentGroupInstanceId == hoveredGroupInstance.InstanceId;
    }
}
