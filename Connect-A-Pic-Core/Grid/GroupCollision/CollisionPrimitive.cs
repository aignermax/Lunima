using CAP_Core.Components.Core;

namespace CAP_Core.Grid.GroupCollision;

/// <summary>
/// One piece of real placed geometry checked by the placement collision test: either a
/// component footprint rectangle or a straight piece of a frozen waveguide centerline
/// with its half-width. Groups never contribute their bounding box — only these pieces.
/// </summary>
internal sealed class CollisionPrimitive
{
    private CollisionPrimitive(
        bool isWaveguide,
        double ax, double ay, double bx, double by,
        double halfWidth,
        IReadOnlyList<Component> owners)
    {
        IsWaveguide = isWaveguide;
        Ax = ax;
        Ay = ay;
        Bx = bx;
        By = by;
        HalfWidth = halfWidth;
        Owners = owners;
        MinX = Math.Min(ax, bx);
        MinY = Math.Min(ay, by);
        MaxX = Math.Max(ax, bx);
        MaxY = Math.Max(ay, by);
    }

    /// <summary>True for a waveguide centerline piece, false for a component footprint.</summary>
    public bool IsWaveguide { get; }

    /// <summary>Piece start X (footprint: min X).</summary>
    public double Ax { get; }

    /// <summary>Piece start Y (footprint: min Y).</summary>
    public double Ay { get; }

    /// <summary>Piece end X (footprint: max X).</summary>
    public double Bx { get; }

    /// <summary>Piece end Y (footprint: max Y).</summary>
    public double By { get; }

    /// <summary>Waveguide half-width in µm; zero for footprints.</summary>
    public double HalfWidth { get; }

    /// <summary>Footprint rectangle, or the centerline piece's axis-aligned box (without width).</summary>
    public double MinX { get; }

    /// <inheritdoc cref="MinX"/>
    public double MinY { get; }

    /// <inheritdoc cref="MinX"/>
    public double MaxX { get; }

    /// <inheritdoc cref="MinX"/>
    public double MaxY { get; }

    /// <summary>
    /// The components this piece belongs to, used as keys for grandfathered contact
    /// pairs: the leaf itself for a footprint, the waveguide's terminal components (or
    /// its owning group when it has no pins) for a waveguide piece.
    /// </summary>
    public IReadOnlyList<Component> Owners { get; }

    /// <summary>Stamp used by the spatial index to report each primitive once per query.</summary>
    internal int QueryStamp { get; set; }

    /// <summary>Creates the footprint rectangle of <paramref name="leaf"/> shifted by the given delta.</summary>
    public static CollisionPrimitive Footprint(Component leaf, double deltaX, double deltaY)
    {
        double x = leaf.PhysicalX + deltaX;
        double y = leaf.PhysicalY + deltaY;
        return new CollisionPrimitive(
            false, x, y, x + leaf.WidthMicrometers, y + leaf.HeightMicrometers, 0, new[] { leaf });
    }

    /// <summary>Creates a footprint rectangle for a free-standing box (e.g. a placement preview).</summary>
    public static CollisionPrimitive Footprint(
        double x, double y, double width, double height, IReadOnlyList<Component> owners) =>
        new(false, x, y, x + width, y + height, 0, owners);

    /// <summary>Creates a waveguide centerline piece.</summary>
    public static CollisionPrimitive Waveguide(
        double ax, double ay, double bx, double by, double halfWidth, IReadOnlyList<Component> owners) =>
        new(true, ax, ay, bx, by, halfWidth, owners);
}
