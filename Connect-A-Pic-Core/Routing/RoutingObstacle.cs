using CAP_Core.Components;

namespace CAP_Core.Routing;

/// <summary>
/// Represents an obstacle that waveguides must route around.
/// </summary>
public class RoutingObstacle
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public Component? SourceComponent { get; set; }

    public bool Contains(double px, double py)
    {
        return px >= X && px <= X + Width && py >= Y && py <= Y + Height;
    }

    public bool Intersects(double x1, double y1, double x2, double y2)
    {
        return LineIntersectsRect(x1, y1, x2, y2, X, Y, Width, Height);
    }

    private static bool LineIntersectsRect(double x1, double y1, double x2, double y2,
                                            double rx, double ry, double rw, double rh)
    {
        return LineIntersectsLine(x1, y1, x2, y2, rx, ry, rx + rw, ry) ||
               LineIntersectsLine(x1, y1, x2, y2, rx + rw, ry, rx + rw, ry + rh) ||
               LineIntersectsLine(x1, y1, x2, y2, rx, ry + rh, rx + rw, ry + rh) ||
               LineIntersectsLine(x1, y1, x2, y2, rx, ry, rx, ry + rh) ||
               (x1 >= rx && x1 <= rx + rw && y1 >= ry && y1 <= ry + rh);
    }

    private static bool LineIntersectsLine(double x1, double y1, double x2, double y2,
                                            double x3, double y3, double x4, double y4)
    {
        double d = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
        if (Math.Abs(d) < 1e-10) return false;

        double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / d;
        double u = -((x1 - x2) * (y1 - y3) - (y1 - y2) * (x1 - x3)) / d;

        return t >= 0 && t <= 1 && u >= 0 && u <= 1;
    }
}
