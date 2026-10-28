namespace CAP_Core.Routing.MeanderGeneration;

/// <summary>
/// Stretches the route between two poses (point + tangent direction) to a prescribed
/// geometric length: the nominal smooth route is computed first, then the extra length
/// is folded into one of its straight sections — as hairpin loops for large extras, or
/// as a double S-bend trim for extras smaller than one loop — choosing the first fold
/// (see <see cref="MeanderCandidateFactory"/>) that stays clear of the obstacles. The result uses the same
/// <see cref="StraightSegment"/>/<see cref="BendSegment"/> primitives the waveguide
/// router produces, so it can be handed to the exporters unchanged.
/// Pure and deterministic: identical inputs yield an identical path.
/// </summary>
public sealed partial class MeanderPathGenerator
{
    private const double GeometrySlackMicrometers = 1e-6;
    private const double BoundsSlackMicrometers = 1e-4;
    private const double PosePositionToleranceMicrometers = 1e-4;
    private const double PoseAngleToleranceDegrees = 1e-4;
    private const double RadiusSlackMicrometers = 1e-9;
    /// <summary>Pin fan-out zone (in bend radii) where sub-spacing proximity to neighbors is tolerated, as in the router.</summary>
    private const double PinFanOutExemptionRadiusFactor = 3.0;

    /// <summary>
    /// Generates a path from the start pose to the end pose whose geometric length is
    /// within tolerance of the target, all bends at or above the minimum radius, fully
    /// inside the bounds and clear of the request's obstacles — or a typed failure
    /// explaining why that is impossible. The compact pin-to-pin route is tried first; when
    /// every fold on it collides, the connection's existing route hosts the fold instead.
    /// </summary>
    public MeanderResult Generate(MeanderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateArguments(request);

        double tolerance = Math.Max(request.ToleranceMicrometers, GeometrySlackMicrometers);
        RoutedPath? nominal = BuildNominalPath(request);
        bool nominalUsable = nominal != null && PoseMatches(nominal, request);
        bool hasExisting = UsableExistingRoute(request, tolerance) != null;
        if (!nominalUsable && !hasExisting)
        {
            return MeanderResult.Failure(
                MeanderFailureReason.EndpointsNotRoutableAtMinRadius,
                "No tangent-continuous route between the two poses exists at the given minimum bend radius.");
        }

        if (nominalUsable && !hasExisting
            && request.TargetLengthMicrometers < nominal!.TotalLengthMicrometers - tolerance)
        {
            return MeanderResult.Failure(
                MeanderFailureReason.TargetShorterThanDirectPath,
                "The target length is shorter than the direct route between the poses.");
        }

        var rejections = new CandidateRejections();
        foreach (var (basePath, rebuilt) in BasePaths(request, nominalUsable ? nominal : null, tolerance))
        {
            var found = SearchBase(basePath, rebuilt, request, tolerance, rejections);
            if (found != null)
                return MeanderResult.Success(found);
        }

        return rejections.Collided
            ? MeanderResult.Failure(MeanderFailureReason.CollidesWithRoutes,
                "Every meander with the required extra length would cross, touch or crowd another route or enter a component.")
            : MeanderResult.Failure(MeanderFailureReason.BoundsTooSmallForMeander,
                "No meander with the required extra length fits inside the bounding rectangle at the given minimum bend radius.");
    }

    private sealed class CandidateRejections
    {
        public bool Collided { get; set; }
    }

    private static IEnumerable<(RoutedPath Path, bool RebuiltPinToPin)> BasePaths(
        MeanderRequest request, RoutedPath? nominal, double tolerance)
    {
        var existing = UsableExistingRoute(request, tolerance);
        if (existing != null && request.PreferExistingRoute)
            yield return (existing, false);
        if (nominal != null && !NominalCollides(nominal, request))
            yield return (nominal, true);
        if (existing != null && !request.PreferExistingRoute)
            yield return (existing, false);
    }

    private static RoutedPath? UsableExistingRoute(MeanderRequest request, double tolerance)
    {
        var existing = request.BasePath;
        if (existing == null || existing.Segments.Count == 0 || existing.IsBlockedFallback || !existing.IsValid)
            return null;
        if (!PoseMatches(existing, request)
            || existing.TotalLengthMicrometers > request.TargetLengthMicrometers + tolerance)
            return null;
        return existing;
    }

    /// <summary>
    /// A rebuilt route that collides before any fold is added cannot be saved by one: a fold
    /// only changes the straight it sits on. Skipping it spares judging every fold on it.
    /// </summary>
    private static bool NominalCollides(RoutedPath nominal, MeanderRequest request) =>
        request.Obstacles is { IsEmpty: false } obstacles
        && MeanderCollisionChecker.Collides(nominal, obstacles,
            request.LoopRadiusMicrometers * PinFanOutExemptionRadiusFactor);

    private RoutedPath? SearchBase(RoutedPath basePath, bool rebuilt, MeanderRequest request,
        double tolerance, CandidateRejections rejections)
    {
        double extra = request.TargetLengthMicrometers - basePath.TotalLengthMicrometers;
        if (Math.Abs(extra) <= tolerance)
        {
            // Already the right length (possibly an empty path between coincident poses): only
            // the bounds and the obstacles can still reject it.
            if (!basePath.Segments.All(s => request.Bounds.Contains(PathSegmentBounds.Of(s), BoundsSlackMicrometers)))
                return null;
            var unchanged = new MeanderCandidate(basePath, rebuilt ? [basePath] : [], rebuilt, []);
            return IsCollisionFree(unchanged, request, rejections) ? basePath : null;
        }
        if (extra < 0)
            return null;

        foreach (var candidate in MeanderCandidateFactory.Enumerate(
                     basePath, rebuilt, extra, request.LoopRadiusMicrometers, request.Obstacles,
                     () => rejections.Collided = true))
        {
            if (Accept(candidate, request, tolerance, rejections))
                return candidate.Path;
        }
        return null;
    }

    private bool Accept(MeanderCandidate candidate, MeanderRequest request, double tolerance,
        CandidateRejections rejections)
    {
        return IsValidCandidate(candidate, request, tolerance) && IsCollisionFree(candidate, request, rejections)
            && KeepsClearOfItself(candidate, request);
    }

    private static bool IsCollisionFree(MeanderCandidate candidate, MeanderRequest request, CandidateRejections rejections)
    {
        if (request.Obstacles is not { IsEmpty: false } obstacles)
            return true;

        double exemption = candidate.RebuiltPinToPin
            ? request.LoopRadiusMicrometers * PinFanOutExemptionRadiusFactor
            : 0.0;
        if (candidate.ChangedPieces.Any(piece => MeanderCollisionChecker.Collides(piece, obstacles, exemption)))
        {
            rejections.Collided = true;
            return false;
        }
        return true;
    }

    private static RoutedPath BuildNominalPath(MeanderRequest request)
    {
        var path = new RoutedPath();
        double dx = request.EndX - request.StartX;
        double dy = request.EndY - request.StartY;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance <= GeometrySlackMicrometers)
        {
            bool sameDirection = AngleDistanceDegrees(
                request.StartDirectionDegrees, request.EndDirectionDegrees) <= PoseAngleToleranceDegrees;
            if (sameDirection)
                return path;
        }
        else
        {
            double lineAngle = Normalize360(Math.Atan2(dy, dx) * 180.0 / Math.PI);
            if (AngleDistanceDegrees(lineAngle, request.StartDirectionDegrees) <= PoseAngleToleranceDegrees
                && AngleDistanceDegrees(lineAngle, request.EndDirectionDegrees) <= PoseAngleToleranceDegrees)
            {
                path.Segments.Add(new StraightSegment(
                    request.StartX, request.StartY, request.EndX, request.EndY, lineAngle));
                return path;
            }
        }

        new ManhattanRouter(request.LoopRadiusMicrometers).Route(
            request.StartX, request.StartY, request.StartDirectionDegrees,
            request.EndX, request.EndY, request.EndDirectionDegrees, path);
        return path;
    }
}
