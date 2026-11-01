using CAP_Core.Routing;

namespace CAP_Core.Components.Core;

/// <summary>
/// The copying steps of <see cref="ComponentGroup.DeepCopy"/> that need no group state: mapping
/// original to copied descendants, copying a leaf component with fresh identity, and copying a
/// frozen route's geometry.
/// </summary>
internal static class GroupCopySupport
{
    /// <summary>
    /// Recursively adds all descendants of a group to the component map.
    /// This is needed because InternalPaths may reference pins from nested group children.
    /// </summary>
    /// <param name="originalGroup">The original group whose descendants to map.</param>
    /// <param name="clonedGroup">The cloned group whose descendants to add to the map.</param>
    /// <param name="componentMap">The map to populate with original ID -> cloned component.</param>
    internal static void AddDescendantsToMap(ComponentGroup originalGroup, ComponentGroup clonedGroup, Dictionary<Guid, Component> componentMap)
    {
        for (int i = 0; i < originalGroup.ChildComponents.Count; i++)
        {
            var originalChild = originalGroup.ChildComponents[i];
            var clonedChild = clonedGroup.ChildComponents[i];

            componentMap[originalChild.Id] = clonedChild;

            if (originalChild is ComponentGroup originalChildGroup && clonedChild is ComponentGroup clonedChildGroup)
            {
                AddDescendantsToMap(originalChildGroup, clonedChildGroup, componentMap);
            }
        }
    }

    /// <summary>
    /// Creates a deep clone of a Component with a new unique identifier.
    /// Delegates to <see cref="Component.Clone"/> so every clone gets its own logical-pin
    /// flow IDs and its own S-matrices — sharing them (as a shallow copy would) makes
    /// copies of one group share simulation state instead of behaving as independent
    /// instances.
    /// </summary>
    internal static Component CloneComponent(Component source)
    {
        var cloned = (Component)source.Clone();
        cloned.Identifier = $"{source.Identifier}_{Guid.NewGuid():N}";
        return cloned;
    }

    /// <summary>
    /// Creates a deep clone of a RoutedPath.
    /// </summary>
    internal static RoutedPath CloneRoutedPath(RoutedPath source)
    {
        var cloned = new RoutedPath
        {
            IsBlockedFallback = source.IsBlockedFallback,
            IsInvalidGeometry = source.IsInvalidGeometry,
            IsPlaceholderGeometry = source.IsPlaceholderGeometry,
            FailureReason = source.FailureReason
        };

        foreach (var segment in source.Segments)
        {
            if (segment is BendSegment bend)
            {
                cloned.Segments.Add(new BendSegment(
                    bend.Center.X,
                    bend.Center.Y,
                    bend.RadiusMicrometers,
                    bend.StartAngleDegrees,
                    bend.SweepAngleDegrees
                ));
            }
            else if (segment is StraightSegment straight)
            {
                cloned.Segments.Add(new StraightSegment(
                    straight.StartPoint.X,
                    straight.StartPoint.Y,
                    straight.EndPoint.X,
                    straight.EndPoint.Y,
                    straight.StartAngleDegrees
                ));
            }
        }

        return cloned;
    }
}
