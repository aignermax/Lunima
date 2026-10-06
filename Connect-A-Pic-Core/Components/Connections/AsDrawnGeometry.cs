using CAP_Core.Components.Core;

namespace CAP_Core.Components.Connections;

/// <summary>
/// The exact polygons an imported waveguide (or other imported top-cell geometry)
/// was drawn with, in absolute canvas coordinates (µm, Y-down). Rendering and
/// export use them verbatim so an untouched import stays byte-faithful to its
/// source file; routing and simulation use the fitted centerline instead.
/// Immutable: a move produces a translated copy.
/// </summary>
public sealed class AsDrawnGeometry
{
    private readonly Lazy<(double MinX, double MinY, double MaxX, double MaxY)> _bounds;

    /// <summary>Creates the geometry from closed polygons in absolute canvas coordinates.</summary>
    /// <param name="polygons">Polygons with their source layer/datatype; must not be empty.</param>
    /// <exception cref="ArgumentException">No polygon was given.</exception>
    public AsDrawnGeometry(IReadOnlyList<OutlinePolygon> polygons)
    {
        ArgumentNullException.ThrowIfNull(polygons);
        if (polygons.Count == 0)
            throw new ArgumentException("As-drawn geometry needs at least one polygon.", nameof(polygons));
        Polygons = polygons;
        _bounds = new Lazy<(double, double, double, double)>(ComputeBounds);
    }

    /// <summary>The polygons, absolute canvas coordinates, first point repeated at the end.</summary>
    public IReadOnlyList<OutlinePolygon> Polygons { get; }

    /// <summary>Axis-aligned bounding box over all polygons (µm).</summary>
    public (double MinX, double MinY, double MaxX, double MaxY) Bounds => _bounds.Value;

    /// <summary>Returns a copy shifted by (<paramref name="dx"/>, <paramref name="dy"/>) µm.</summary>
    public AsDrawnGeometry Translated(double dx, double dy) =>
        new(Polygons.Select(p => p with
        {
            Points = p.Points.Select(pt => new OutlinePoint(pt.X + dx, pt.Y + dy)).ToList(),
        }).ToList());

    private (double, double, double, double) ComputeBounds()
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var point in Polygons.SelectMany(p => p.Points))
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }
        return (minX, minY, maxX, maxY);
    }
}
