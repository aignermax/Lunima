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

    /// <summary>
    /// Every contact between the moved geometry and the obstacles, keyed by moved owner,
    /// or — with <paramref name="stopAtFirst"/> — just whether there is one (empty map = none).
    /// </summary>
    /// <param name="obstacleRoots">Top-level obstacles (plain components or groups).</param>
    /// <param name="skip">Components (and their descendants) that never act as obstacles.</param>
    /// <param name="stopAtFirst">Return as soon as one contact is found.</param>
    public Dictionary<Component, HashSet<Component>> FindContacts(
        IEnumerable<Component> obstacleRoots, IReadOnlySet<Component> skip, bool stopAtFirst)
    {
        var contacts = new Dictionary<Component, HashSet<Component>>();
        if (_moved.Count == 0)
            return contacts;

        var obstacles = CollectNearbyObstacles(obstacleRoots, skip);
        if (obstacles.Count == 0)
            return contacts;

        double obstacleMaxHalfWidth = obstacles.Max(o => o.HalfWidth);
        double reach = BaseReach + _movedMaxHalfWidth + obstacleMaxHalfWidth;
        var index = new CollisionSpatialIndex(obstacles);
        foreach (var moved in _moved)
        {
            bool stop = index.AnyNear(
                moved.MinX - moved.HalfWidth, moved.MinY - moved.HalfWidth,
                moved.MaxX + moved.HalfWidth, moved.MaxY + moved.HalfWidth, reach,
                obstacle =>
                {
                    if (IsGrandfathered(moved, obstacle) || !Collides(moved, obstacle))
                        return false;
                    Record(contacts, moved, obstacle);
                    return stopAtFirst;
                });
            if (stop)
                break;
        }
        return contacts;
    }

    /// <summary>True when the two primitives violate the placement clearance rules.</summary>
    internal static bool Collides(CollisionPrimitive a, CollisionPrimitive b)
    {
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

    private List<CollisionPrimitive> CollectNearbyObstacles(IEnumerable<Component> roots, IReadOnlySet<Component> skip)
    {
        // Obstacle waveguide widths are unknown before expansion, so the coarse reject keeps a
        // generous margin; the exact rules run afterwards.
        double margin = BaseReach + _movedMaxHalfWidth + CoarseWaveguideMarginMicrometers;
        var obstacles = new List<CollisionPrimitive>();
        foreach (var root in roots)
        {
            if (skip.Contains(root))
                continue;
            var (minX, minY, maxX, maxY) = CollisionShapeBuilder.Bounds(root);
            if (minX > _movedBounds.MaxX + margin || maxX < _movedBounds.MinX - margin
                || minY > _movedBounds.MaxY + margin || maxY < _movedBounds.MinY - margin)
                continue;
            CollisionShapeBuilder.Append(root, 0, 0, obstacles, skip);
        }
        return obstacles;
    }

    /// <summary>Margin added to the coarse per-component reject (bounds already include waveguide half-widths).</summary>
    private const double CoarseWaveguideMarginMicrometers = 1.0;

    private bool IsGrandfathered(CollisionPrimitive moved, CollisionPrimitive obstacle)
    {
        if (_partners is null)
            return false;
        foreach (var owner in moved.Owners)
        {
            if (!_partners.TryGetValue(owner, out var partners))
                continue;
            foreach (var other in obstacle.Owners)
            {
                if (partners.Contains(other))
                    return true;
            }
        }
        return false;
    }

    private static void Record(
        Dictionary<Component, HashSet<Component>> contacts, CollisionPrimitive moved, CollisionPrimitive obstacle)
    {
        foreach (var owner in moved.Owners)
        {
            if (!contacts.TryGetValue(owner, out var set))
            {
                set = new HashSet<Component>();
                contacts[owner] = set;
            }
            set.UnionWith(obstacle.Owners);
        }
    }
}
