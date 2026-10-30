namespace CAP_Core.Routing.MeanderGeneration;

/// <summary>
/// Validation half of <see cref="MeanderPathGenerator"/>: argument checks and the
/// geometric post-checks every emitted path must pass (length, bend-radius floor,
/// bounds, continuity, endpoint poses) before it may leave the generator.
/// </summary>
public sealed partial class MeanderPathGenerator
{
    /// <summary>
    /// Length, poses and bounds of the whole path; the radius floor only for the new geometry —
    /// an existing route with tighter (e.g. imported) bends may still host a fold.
    /// </summary>
    private bool IsValidCandidate(MeanderCandidate candidate, MeanderRequest request, double tolerance)
    {
        var path = candidate.Path;
        if (!path.IsValid || !PoseMatches(path, request))
            return false;
        if (Math.Abs(path.TotalLengthMicrometers - request.TargetLengthMicrometers) > tolerance)
            return false;
        if (!path.Segments.All(s => request.Bounds.Contains(PathSegmentBounds.Of(s), BoundsSlackMicrometers)))
            return false;

        return candidate.ChangedPieces.SelectMany(piece => piece.Segments).OfType<BendSegment>()
            .All(bend => bend.RadiusMicrometers >= request.MinBendRadiusMicrometers - RadiusSlackMicrometers);
    }

    /// <summary>
    /// The folds keep the spacing to the rest of their own route: a loop on one leg of a U-shaped
    /// route must not reach the other leg. The straights' direct neighbours join the fold and
    /// are left out.
    /// </summary>
    private static bool KeepsClearOfItself(MeanderCandidate candidate, MeanderRequest request)
    {
        if (candidate.RebuiltPinToPin)
            return !PathIntersectionDetector.HasSelfIntersection(candidate.Path);
        if (candidate.HostIndices.Count == 0 || request.BasePath is not { } basePath)
            return true;

        var rest = new RoutedPath();
        for (int i = 0; i < basePath.Segments.Count; i++)
        {
            if (!candidate.HostIndices.Any(host => Math.Abs(host - i) <= 1))
                rest.Segments.Add(basePath.Segments[i]);
        }
        if (rest.Segments.Count == 0)
            return true;
        double spacing = request.Obstacles?.MinSpacingMicrometers ?? 0.0;
        double contact = request.Obstacles?.ContactClearanceMicrometers ?? 0.0;
        var self = new MeanderObstacles([rest], [], spacing, contact);
        return !candidate.ChangedPieces.Any(piece => MeanderCollisionChecker.Collides(piece, self, 0.0));
    }

    private static bool PoseMatches(RoutedPath path, MeanderRequest request)
    {
        if (path.Segments.Count == 0)
        {
            double dx = request.EndX - request.StartX;
            double dy = request.EndY - request.StartY;
            return Math.Sqrt(dx * dx + dy * dy) <= PosePositionToleranceMicrometers;
        }

        var first = path.Segments[0];
        var last = path.Segments[^1];
        return Distance(first.StartPoint, (request.StartX, request.StartY)) <= PosePositionToleranceMicrometers
            && Distance(last.EndPoint, (request.EndX, request.EndY)) <= PosePositionToleranceMicrometers
            && AngleDistanceDegrees(first.StartAngleDegrees, request.StartDirectionDegrees) <= PoseAngleToleranceDegrees
            && AngleDistanceDegrees(last.EndAngleDegrees, request.EndDirectionDegrees) <= PoseAngleToleranceDegrees;
    }

    private static void ValidateArguments(MeanderRequest request)
    {
        if (!double.IsFinite(request.StartX) || !double.IsFinite(request.StartY)
            || !double.IsFinite(request.EndX) || !double.IsFinite(request.EndY)
            || !double.IsFinite(request.StartDirectionDegrees) || !double.IsFinite(request.EndDirectionDegrees)
            || !double.IsFinite(request.TargetLengthMicrometers) || !double.IsFinite(request.ToleranceMicrometers)
            || !double.IsFinite(request.MinBendRadiusMicrometers))
            throw new ArgumentException("All request values must be finite.", nameof(request));
        if (request.MinBendRadiusMicrometers <= 0)
            throw new ArgumentException("Minimum bend radius must be positive.", nameof(request));
        if (request.TargetLengthMicrometers < 0)
            throw new ArgumentException("Target length must not be negative.", nameof(request));
        if (request.ToleranceMicrometers < 0)
            throw new ArgumentException("Tolerance must not be negative.", nameof(request));
        if (request.Bounds.MaxX <= request.Bounds.MinX || request.Bounds.MaxY <= request.Bounds.MinY)
            throw new ArgumentException("Bounds must have positive width and height.", nameof(request));
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Normalize360(double angleDeg)
    {
        angleDeg %= 360.0;
        if (angleDeg < 0.0)
            angleDeg += 360.0;
        return angleDeg;
    }

    private static double AngleDistanceDegrees(double aDeg, double bDeg)
    {
        double delta = Normalize360(aDeg) - Normalize360(bDeg);
        if (delta < 0.0)
            delta += 360.0;
        return Math.Min(delta, 360.0 - delta);
    }
}
