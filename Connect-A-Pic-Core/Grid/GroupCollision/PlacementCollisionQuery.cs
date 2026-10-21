using CAP_Core.Components.Core;

namespace CAP_Core.Grid.GroupCollision;

/// <summary>
/// Tests moved geometry (footprints + waveguide pieces) against the obstacle geometry of
/// everything else on the canvas with the placement clearance rules:
/// <list type="bullet">
/// <item>footprint ↔ footprint: closer than <see cref="MinComponentGapMicrometers"/> on both axes;</item>
/// <item>waveguide ↔ footprint: centerline closer than half-width + <see cref="MinWaveguideSpacingMicrometers"/>;</item>
/// <item>waveguide ↔ waveguide: centerlines closer than both half-widths + <see cref="MinWaveguideSpacingMicrometers"/>.</item>
/// </list>
/// Obstacles are only expanded into primitives when their cheap outer bounds come near the
/// moved geometry, and are spatially indexed, so large groups stay fast.
/// </summary>
internal sealed class PlacementCollisionQuery
{
    /// <summary>Minimum gap between component footprints (µm).</summary>
    public const double MinComponentGapMicrometers = 5.0;

    /// <summary>Minimum edge-to-edge spacing between a waveguide and other geometry (µm).</summary>
    public const double MinWaveguideSpacingMicrometers = PhotonicConstants.DefaultMinWaveguideSpacingMicrometers;

    private readonly List<CollisionPrimitive> _moved;
    private readonly IReadOnlyDictionary<Component, HashSet<Component>>? _partners;
    private readonly double _movedMaxHalfWidth;
    private readonly (double MinX, double MinY, double MaxX, double MaxY) _movedBounds;

    /// <summary>Creates a query for the given moved geometry.</summary>
    /// <param name="moved">The geometry at its candidate position.</param>
    /// <param name="partners">
    /// Grandfathered contacts keyed by a moved primitive's owner: pairs listed here never collide.
    /// </param>
    public PlacementCollisionQuery(
        List<CollisionPrimitive> moved, IReadOnlyDictionary<Component, HashSet<Component>>? partners)
    {
        _moved = moved;
        _partners = partners;
        _movedMaxHalfWidth = moved.Count == 0 ? 0 : moved.Max(p => p.HalfWidth);
        _movedBounds = moved.Count == 0
            ? (0, 0, 0, 0)
            : (moved.Min(p => p.MinX - p.HalfWidth), moved.Min(p => p.MinY - p.HalfWidth),
               moved.Max(p => p.MaxX + p.HalfWidth), moved.Max(p => p.MaxY + p.HalfWidth));
    }

    /// <summary>Largest distance at which any rule can still report a collision, before waveguide widths.</summary>
    private static double BaseReach => Math.Max(MinComponentGapMicrometers, MinWaveguideSpacingMicrometers);

    /// <summary>True when any moved primitive violates a clearance rule against an obstacle.</summary>
    /// <param name="obstacleRoots">Top-level obstacles (plain components or groups).</param>
    /// <param name="skip">Components (and their descendants) that never act as obstacles.</param>
    public bool HasCollision(IEnumerable<Component> obstacleRoots, IReadOnlySet<Component> skip) =>
        Scan(obstacleRoots, skip, (_, _) => true);

    /// <summary>Every contact between the moved geometry and the obstacles, keyed by moved owner.</summary>
    /// <param name="obstacleRoots">Top-level obstacles (plain components or groups).</param>
    /// <param name="skip">Components (and their descendants) that never act as obstacles.</param>
    public Dictionary<Component, HashSet<Component>> FindContacts(
        IEnumerable<Component> obstacleRoots, IReadOnlySet<Component> skip)
    {
        var contacts = new Dictionary<Component, HashSet<Component>>();
        Scan(obstacleRoots, skip, (moved, obstacle) =>
        {
            Record(contacts, moved, obstacle);
            return false;
        });
        return contacts;
    }

    /// <summary>
    /// Reports each non-grandfathered colliding (moved, obstacle) pair to
    /// <paramref name="onContact"/>; stops and returns true as soon as it returns true.
    /// </summary>
    private bool Scan(
        IEnumerable<Component> obstacleRoots, IReadOnlySet<Component> skip,
        Func<CollisionPrimitive, CollisionPrimitive, bool> onContact)
    {
        if (_moved.Count == 0)
            return false;
        var obstacles = CollectNearbyObstacles(obstacleRoots, skip);
        if (obstacles.Count == 0)
            return false;

        double reach = BaseReach + _movedMaxHalfWidth + obstacles.Max(o => o.HalfWidth);
        var index = new CollisionSpatialIndex(obstacles);
        foreach (var moved in _moved)
        {
            bool stop = index.AnyNear(
                moved.MinX - moved.HalfWidth, moved.MinY - moved.HalfWidth,
                moved.MaxX + moved.HalfWidth, moved.MaxY + moved.HalfWidth, reach,
                obstacle => !IsGrandfathered(moved, obstacle) && Collides(moved, obstacle) && onContact(moved, obstacle));
            if (stop)
                return true;
        }
        return false;
    }

    /// <summary>True when the two primitives violate the placement clearance rules.</summary>
    internal static bool Collides(CollisionPrimitive a, CollisionPrimitive b)
    {
        if (a.Polygon is not null || b.Polygon is not null)
        {
            var (polygon, other) = a.Polygon is not null ? (a, b) : (b, a);
            if (other.Polygon is not null)
                return OutlineCollisionGeometry.WithinDistance(polygon.Polygon!, other.Polygon,
                    MinComponentGapMicrometers, polygon.Owner!);
            double distance = other.IsWaveguide
                    ? OutlineCollisionGeometry.ToSegment(polygon.Polygon!, other.Ax, other.Ay, other.Bx, other.By)
                    : OutlineCollisionGeometry.ToRectangle(polygon.Polygon!, other.MinX, other.MinY, other.MaxX, other.MaxY);
            return distance < (other.IsWaveguide
                ? other.HalfWidth + MinWaveguideSpacingMicrometers : MinComponentGapMicrometers);
        }
        if (!a.IsWaveguide && !b.IsWaveguide)
        {
            double gap = MinComponentGapMicrometers;
            return a.MinX - gap < b.MaxX && a.MaxX + gap > b.MinX
                && a.MinY - gap < b.MaxY && a.MaxY + gap > b.MinY;
        }

        double clearance = a.HalfWidth + b.HalfWidth + MinWaveguideSpacingMicrometers;
        if (a.IsWaveguide && b.IsWaveguide)
            return CollisionDistance.SegmentToSegment(a.Ax, a.Ay, a.Bx, a.By, b.Ax, b.Ay, b.Bx, b.By) < clearance;

        var (wave, box) = a.IsWaveguide ? (a, b) : (b, a);
        return CollisionDistance.SegmentToRect(
            wave.Ax, wave.Ay, wave.Bx, wave.By, box.MinX, box.MinY, box.MaxX, box.MaxY) < clearance;
    }

    /// <summary>
    /// Expands only the obstacle geometry near the moved geometry: the window is the moved
    /// extent grown by the largest rule distance (obstacle waveguide bounds already carry
    /// their half-width), plus a little slack against floating-point touches.
    /// </summary>
    private List<CollisionPrimitive> CollectNearbyObstacles(IEnumerable<Component> roots, IReadOnlySet<Component> skip)
    {
        double margin = BaseReach + WindowSlackMicrometers;
        var window = new CollisionWindow(
            _movedBounds.MinX - margin, _movedBounds.MinY - margin,
            _movedBounds.MaxX + margin, _movedBounds.MaxY + margin);
        var obstacles = new List<CollisionPrimitive>();
        foreach (var root in roots)
            CollisionShapeBuilder.Append(root, 0, 0, obstacles, skip, window);
        return obstacles;
    }

    /// <summary>Slack (µm) on the obstacle window so touching geometry is never pruned.</summary>
    private const double WindowSlackMicrometers = 1.0;

    private bool IsGrandfathered(CollisionPrimitive moved, CollisionPrimitive obstacle)
    {
        return _partners is not null
            && moved.Owner is not null
            && obstacle.Owner is not null
            && _partners.TryGetValue(moved.Owner, out var partners)
            && partners.Contains(obstacle.Owner);
    }

    private static void Record(
        Dictionary<Component, HashSet<Component>> contacts, CollisionPrimitive moved, CollisionPrimitive obstacle)
    {
        if (moved.Owner is null || obstacle.Owner is null)
            return;
        if (!contacts.TryGetValue(moved.Owner, out var set))
        {
            set = new HashSet<Component>();
            contacts[moved.Owner] = set;
        }
        set.Add(obstacle.Owner);
    }
}
