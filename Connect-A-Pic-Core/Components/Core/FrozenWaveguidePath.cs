using CAP_Core.Routing;

namespace CAP_Core.Components.Core;

/// <summary>
/// Represents a waveguide path stored in a ComponentGroup template.
/// Used ONLY for template storage - when templates are instantiated via PlaceTemplateCommand,
/// these frozen paths are converted to regular WaveguideConnections that are auto-routed.
///
/// Purpose:
/// - Store connection topology in templates (which pins connect to which)
/// - Preserve approximate path geometry for template preview
/// - Enable template instantiation without re-computing connections from scratch
///
/// NOT USED FOR:
/// - Live connections on canvas (use WaveguideConnection instead)
/// - Runtime routing (frozen paths are discarded after instantiation)
/// - Nested groups (groups are template-only, never live on canvas)
///
/// Lifecycle:
/// 1. Template creation: WaveguideConnections → FrozenWaveguidePaths (stored in ComponentGroup)
/// 2. Template storage: Serialized to JSON with ComponentGroup
/// 3. Template instantiation: FrozenWaveguidePaths → WaveguideConnections (via PlaceTemplateCommand)
///
/// See docs/ComponentGroup-Architecture.md for detailed design documentation.
/// </summary>
public class FrozenWaveguidePath : ICloneable
{
    /// <summary>
    /// The routed path segments with fixed geometry.
    /// This stores the exact path shape from template creation, but when instantiated,
    /// PlaceTemplateCommand creates new auto-routed connections (not using this geometry).
    /// </summary>
    public RoutedPath Path { get; set; }

    /// <summary>
    /// Physical pin where this frozen path starts (reference to component in template).
    /// When instantiating, PlaceTemplateCommand finds the corresponding pin in placed components.
    /// </summary>
    public PhysicalPin StartPin { get; set; }

    /// <summary>
    /// Physical pin where this frozen path ends (reference to component in template).
    /// When instantiating, PlaceTemplateCommand finds the corresponding pin in placed components.
    /// </summary>
    public PhysicalPin EndPin { get; set; }

    /// <summary>
    /// Unique identifier for this frozen path (for template serialization).
    /// </summary>
    public Guid PathId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Translates all segments in the path by the specified delta.
    /// Used when moving the containing ComponentGroup during template creation/editing.
    /// NOTE: This is only called during template manipulation in the library, NOT for
    /// live canvas operations (since frozen paths are never on canvas).
    /// </summary>
    /// <param name="deltaX">X offset in micrometers.</param>
    /// <param name="deltaY">Y offset in micrometers.</param>
    public void TranslateBy(double deltaX, double deltaY)
    {
        if (Path?.Segments == null) return;

        foreach (var segment in Path.Segments)
        {
            segment.StartPoint = (
                segment.StartPoint.X + deltaX,
                segment.StartPoint.Y + deltaY
            );
            segment.EndPoint = (
                segment.EndPoint.X + deltaX,
                segment.EndPoint.Y + deltaY
            );

            // If it's a bend segment, translate the center point as well
            if (segment is BendSegment bend)
            {
                bend.Center = (
                    bend.Center.X + deltaX,
                    bend.Center.Y + deltaY
                );
            }
        }
    }

    /// <summary>
    /// Creates a deep clone of this frozen path with a new PathId.
    /// Used by ComponentGroup.DeepCopy() when instantiating templates.
    /// The cloned path has copied segment geometry but StartPin and EndPin
    /// references must be updated by the caller to point to cloned components.
    /// </summary>
    public object Clone()
    {
        var clonedPath = new RoutedPath
        {
            IsBlockedFallback = Path.IsBlockedFallback,
            IsInvalidGeometry = Path.IsInvalidGeometry
        };

        // Deep clone all segments
        foreach (var segment in Path.Segments)
        {
            if (segment is BendSegment bend)
            {
                clonedPath.Segments.Add(new BendSegment(
                    bend.Center.X,
                    bend.Center.Y,
                    bend.RadiusMicrometers,
                    bend.StartAngleDegrees,
                    bend.SweepAngleDegrees
                ));
            }
            else if (segment is StraightSegment straight)
            {
                clonedPath.Segments.Add(new StraightSegment(
                    straight.StartPoint.X,
                    straight.StartPoint.Y,
                    straight.EndPoint.X,
                    straight.EndPoint.Y,
                    straight.StartAngleDegrees
                ));
            }
        }

        return new FrozenWaveguidePath
        {
            Path = clonedPath,
            PathId = Guid.NewGuid(),
            // StartPin and EndPin references must be updated after cloning by the ComponentGroup
        };
    }
}
