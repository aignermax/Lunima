using System;

namespace CAP_Core.Components.Dies;

/// <summary>Axis-aligned die outline in absolute canvas micrometers.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Outline width.</param>
/// <param name="Height">Outline height.</param>
public readonly record struct DieOutline(double X, double Y, double Width, double Height)
{
    /// <summary>Right edge.</summary>
    public double Right => X + Width;

    /// <summary>Bottom edge.</summary>
    public double Bottom => Y + Height;

    /// <summary>Horizontal center.</summary>
    public double CenterX => X + Width / 2;

    /// <summary>Vertical center.</summary>
    public double CenterY => Y + Height / 2;

    /// <summary>True when the point lies inside or on the outline.</summary>
    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;

    /// <summary>True when the rectangle lies completely inside the outline (edges included, within <paramref name="tolerance"/>).</summary>
    public bool ContainsRect(double x, double y, double width, double height, double tolerance = 1e-6) =>
        x >= X - tolerance && y >= Y - tolerance
        && x + width <= Right + tolerance && y + height <= Bottom + tolerance;

    /// <summary>True when the two outlines share interior area (touching edges do not count).</summary>
    public bool Overlaps(DieOutline other) =>
        X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;

    /// <summary>The outline moved by (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
    public DieOutline Translated(double dx, double dy) => this with { X = X + dx, Y = Y + dy };

    /// <summary>The smallest outline enclosing both.</summary>
    public DieOutline Union(DieOutline other)
    {
        double minX = Math.Min(X, other.X);
        double minY = Math.Min(Y, other.Y);
        return new DieOutline(minX, minY, Math.Max(Right, other.Right) - minX, Math.Max(Bottom, other.Bottom) - minY);
    }
}
