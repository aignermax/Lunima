using CAP_Core.Routing;

namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Builds waveguide path segments using Manhattan routing.
/// Splits complex MoveToPointFollowingDirection logic into focused methods.
/// </summary>
public class PathBuilder
{
    private readonly BendBuilder _bendBuilder;
    private readonly SBendBuilder _sBendBuilder;
    private readonly PathGeometryAnalyzer _analyzer;
    private readonly double _minBendRadius;

    public PathBuilder(BendBuilder bendBuilder, SBendBuilder sBendBuilder,
                       PathGeometryAnalyzer analyzer, double minBendRadius)
    {
        _bendBuilder = bendBuilder;
        _sBendBuilder = sBendBuilder;
        _analyzer = analyzer;
        _minBendRadius = minBendRadius;
    }

    /// <summary>
    /// Moves from current position to target point following specified travel angle.
    /// Chooses between horizontal-first, vertical-first, or single-direction routing.
    /// </summary>
    public void MoveToPoint(RoutedPath path, ref double x, ref double y, ref double angle,
                            double targetX, double targetY, double travelAngle)
    {
        double dx = targetX - x;
        double dy = targetY - y;

        bool needH = Math.Abs(dx) > 0.5;
        bool needV = Math.Abs(dy) > 0.5;

        if (needH && needV)
        {
            // L-shape routing - choose direction order based on A* travel angle
            // ALWAYS respect A* path, don't override based on current angle
            bool preferHorizontalFirst = AngleUtilities.IsHorizontal(travelAngle);

            if (preferHorizontalFirst)
            {
                MoveHorizontalFirst(path, ref x, ref y, ref angle, targetX, targetY, dx, dy);
            }
            else
            {
                MoveVerticalFirst(path, ref x, ref y, ref angle, targetX, targetY, dx, dy);
            }
        }
        else if (needH)
        {
            // Horizontal only
            MoveHorizontal(path, ref x, ref y, ref angle, targetX, dx);
        }
        else if (needV)
        {
            // Vertical only
            MoveVertical(path, ref x, ref y, ref angle, targetY, dy);
        }
    }

    /// <summary>
    /// L-shape routing: Horizontal first, then vertical.
    /// </summary>
    private void MoveHorizontalFirst(RoutedPath path, ref double x, ref double y, ref double angle,
                                      double targetX, double targetY, double dx, double dy)
    {
        double hAngle = dx > 0 ? 0 : 180;
        double vAngle = dy > 0 ? 90 : 270;
        double bendRadius = _minBendRadius;

        // Turn to horizontal if needed
        if (!AngleUtilities.IsAngleClose(angle, hAngle))
        {
            var bend = _bendBuilder.BuildBend(x, y, angle, hAngle, BendMode.Cardinal90);
            if (bend != null)
            {
                path.Segments.Add(bend);
                x = bend.EndPoint.X;
                y = bend.EndPoint.Y;
                angle = hAngle;
            }
        }

        // Go horizontal (leave room for final bend)
        double hDist = Math.Abs(dx) - bendRadius;
        if (hDist > 0.5)
        {
            // Check if we should insert a correction S-bend to approach A* path
            if (hDist > bendRadius * 4)
            {
                TryInsertCorrectionSBend(path, ref x, ref y, ref angle, targetX, targetY, hAngle);
            }

            // Continue horizontal to target (or to where correction S-bend left us)
            double remainingH = targetX - x - Math.Sign(dx) * bendRadius;
            if (Math.Abs(remainingH) > 0.5)
            {
                AddStraight(path, ref x, ref y, x + remainingH, y, angle);
            }
        }

        // Turn to vertical
        var vBend = _bendBuilder.BuildBend(x, y, angle, vAngle, BendMode.Cardinal90);
        if (vBend != null)
        {
            path.Segments.Add(vBend);
            x = vBend.EndPoint.X;
            y = vBend.EndPoint.Y;
            angle = vAngle;
        }

        // Go vertical to target
        if (Math.Abs(targetY - y) > 0.5)
        {
            AddStraight(path, ref x, ref y, x, targetY, angle);
        }
    }

    /// <summary>
    /// L-shape routing: Vertical first, then horizontal.
    /// </summary>
    private void MoveVerticalFirst(RoutedPath path, ref double x, ref double y, ref double angle,
                                    double targetX, double targetY, double dx, double dy)
    {
        double hAngle = dx > 0 ? 0 : 180;
        double vAngle = dy > 0 ? 90 : 270;
        double bendRadius = _minBendRadius;

        // Turn to vertical if needed
        if (!AngleUtilities.IsAngleClose(angle, vAngle))
        {
            var bend = _bendBuilder.BuildBend(x, y, angle, vAngle, BendMode.Cardinal90);
            if (bend != null)
            {
                path.Segments.Add(bend);
                x = bend.EndPoint.X;
                y = bend.EndPoint.Y;
                angle = vAngle;
            }
        }

        // Go vertical (leave room for final bend)
        double vDist = Math.Abs(dy) - bendRadius;
        if (vDist > 0.5)
        {
            // Check if we should insert a correction S-bend to approach A* path
            if (vDist > bendRadius * 4)
            {
                TryInsertCorrectionSBend(path, ref x, ref y, ref angle, targetX, targetY, vAngle);
            }

            // Continue vertical to target (or to where correction S-bend left us)
            double remainingV = targetY - y - Math.Sign(dy) * bendRadius;
            if (Math.Abs(remainingV) > 0.5)
            {
                AddStraight(path, ref x, ref y, x, y + remainingV, angle);
            }
        }

        // Turn to horizontal
        var hBend = _bendBuilder.BuildBend(x, y, angle, hAngle, BendMode.Cardinal90);
        if (hBend != null)
        {
            path.Segments.Add(hBend);
            x = hBend.EndPoint.X;
            y = hBend.EndPoint.Y;
            angle = hAngle;
        }

        // Go horizontal to target
        if (Math.Abs(targetX - x) > 0.5)
        {
            AddStraight(path, ref x, ref y, targetX, y, angle);
        }
    }

    /// <summary>
    /// Single-direction horizontal movement.
    /// </summary>
    private void MoveHorizontal(RoutedPath path, ref double x, ref double y, ref double angle,
                                double targetX, double dx)
    {
        double hAngle = dx > 0 ? 0 : 180;

        // Turn to horizontal if needed
        if (!AngleUtilities.IsAngleClose(angle, hAngle))
        {
            var bend = _bendBuilder.BuildBend(x, y, angle, hAngle, BendMode.Cardinal90);
            if (bend != null)
            {
                path.Segments.Add(bend);
                x = bend.EndPoint.X;
                y = bend.EndPoint.Y;
                angle = hAngle;
            }
        }

        // Go horizontal to target
        AddStraight(path, ref x, ref y, targetX, y, angle);
    }

    /// <summary>
    /// Single-direction vertical movement.
    /// </summary>
    private void MoveVertical(RoutedPath path, ref double x, ref double y, ref double angle,
                              double targetY, double dy)
    {
        double vAngle = dy > 0 ? 90 : 270;

        // Turn to vertical if needed
        if (!AngleUtilities.IsAngleClose(angle, vAngle))
        {
            var bend = _bendBuilder.BuildBend(x, y, angle, vAngle, BendMode.Cardinal90);
            if (bend != null)
            {
                path.Segments.Add(bend);
                x = bend.EndPoint.X;
                y = bend.EndPoint.Y;
                angle = vAngle;
            }
        }

        // Go vertical to target
        AddStraight(path, ref x, ref y, x, targetY, angle);
    }

    /// <summary>
    /// Adds a straight segment to the path.
    /// </summary>
    private void AddStraight(RoutedPath path, ref double x, ref double y,
                             double endX, double endY, double angle)
    {
        path.Segments.Add(new StraightSegment(x, y, endX, endY, angle));
        x = endX;
        y = endY;
    }

    /// <summary>
    /// Attempts to insert a correction S-bend if waveguide has drifted from A* path.
    /// </summary>
    private void TryInsertCorrectionSBend(RoutedPath path, ref double x, ref double y, ref double angle,
                                           double targetX, double targetY, double travelAngle)
    {
        // Calculate lateral offset to A* path
        var (offset, correctionAngle) = _analyzer.CalculateLateralOffset(x, y, angle);

        // Delegate to SBendBuilder
        _sBendBuilder.TryBuildCorrectionSBend(
            path, ref x, ref y, ref angle,
            offset, correctionAngle, travelAngle,
            targetX, targetY);
    }
}
