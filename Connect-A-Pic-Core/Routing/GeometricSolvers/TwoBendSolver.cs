using CAP_Core.Components;
using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Routing.Utilities;

namespace CAP_Core.Routing.GeometricSolvers;

/// <summary>
/// Attempts to connect two pins using geometric solutions (straight line or two bends).
/// High-level orchestrator that delegates to specialized helper classes.
/// </summary>
public class TwoBendSolver
{
    private readonly double _minBendRadius;
    private readonly List<double> _allowedRadii;
    private readonly BiarcSolver _biarcSolver;
    private readonly GeometricValidator _validator;
    private readonly ObstacleChecker _obstacleChecker;

    public TwoBendSolver(double minBendRadius, List<double>? allowedRadii, WaveguideRouter router)
    {
        _minBendRadius = minBendRadius;
        _allowedRadii = allowedRadii ?? new List<double>();
        _biarcSolver = new BiarcSolver();
        _validator = new GeometricValidator();
        _obstacleChecker = new ObstacleChecker(router.PathfindingGrid);
    }

    /// <summary>
    /// Attempts to connect two pins using geometric solutions.
    /// Tries: straight line → two bends (LL/LR/RL/RR).
    /// </summary>
    public RoutedPath? TryTwoBendConnection(PhysicalPin startPin, PhysicalPin endPin)
    {
        Console.WriteLine("[TwoBendSolver] Attempting connection...");

        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();
        double startAngle = startPin.GetAbsoluteAngle();
        double endAngle = endPin.GetAbsoluteAngle();
        double endEntryAngle = AngleUtilities.NormalizeAngle(endAngle + 180);

        // FIRST: Try straight line (zero bends)
        var straightPath = TryStraightLine(startX, startY, startAngle, endX, endY, endEntryAngle);
        if (straightPath != null)
        {
            Console.WriteLine("[TwoBendSolver] SUCCESS with straight line!");
            return straightPath;
        }

        // SECOND: Try all four bend combinations
        var bendCombinations = new[]
        {
            (1.0, 1.0, "LL"),    // Left-Left
            (1.0, -1.0, "LR"),   // Left-Right (S-bend)
            (-1.0, 1.0, "RL"),   // Right-Left (S-bend)
            (-1.0, -1.0, "RR")   // Right-Right
        };

        foreach (var (firstDir, secondDir, label) in bendCombinations)
        {
            Console.WriteLine($"[TwoBendSolver]   Trying {label}...");

            var path = TryBuildTwoBendPath(
                startX, startY, startAngle,
                endX, endY, endEntryAngle,
                firstDir, secondDir);

            if (path != null)
            {
                Console.WriteLine($"[TwoBendSolver]   SUCCESS with {label}!");
                return path;
            }
        }

        Console.WriteLine("[TwoBendSolver] No valid solution found");
        return null;
    }

    /// <summary>
    /// Attempts a straight-line connection (zero bends).
    /// </summary>
    private RoutedPath? TryStraightLine(
        double startX, double startY, double startAngle,
        double endX, double endY, double endEntryAngle)
    {
        // Check if angles are aligned
        if (!_validator.ValidateAngleAlignment(startAngle, endEntryAngle))
            return null;

        // Check if line is aligned with start direction
        if (!_validator.ValidateLineAlignment(startX, startY, endX, endY, startAngle))
            return null;

        // Check for obstacles
        if (_obstacleChecker.IsLineBlocked(startX, startY, endX, endY))
            return null;

        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(startX, startY, endX, endY, startAngle));
        return path;
    }

    /// <summary>
    /// Attempts a two-bend connection with specified bend directions.
    /// Tries multiple radii to find a valid solution.
    /// </summary>
    private RoutedPath? TryBuildTwoBendPath(
        double startX, double startY, double startAngle,
        double endX, double endY, double endEntryAngle,
        double firstBendDir, double secondBendDir)
    {
        // Get radii to try
        var radiiToTry = GetRadiiToTry();

        foreach (double radius in radiiToTry)
        {
            // Solve biarc problem
            var result = _biarcSolver.SolveBiarc(
                startX, startY, startAngle,
                endX, endY, endEntryAngle,
                firstBendDir, secondBendDir,
                radius);

            if (result == null)
                continue;

            var (bend1, bend2) = result.Value;

            // Validate geometry
            if (!ValidateBiarc(bend1, bend2, startX, startY, endX, endY))
                continue;

            // Check obstacles
            if (_obstacleChecker.IsArcBlocked(bend1) || _obstacleChecker.IsArcBlocked(bend2))
            {
                Console.WriteLine($"[TwoBendSolver]     Blocked by obstacles");
                continue;
            }

            // Success!
            var path = new RoutedPath();
            path.Segments.Add(bend1);
            path.Segments.Add(bend2);
            return path;
        }

        return null;
    }

    /// <summary>
    /// Validates that a biarc (two arcs) has correct geometry.
    /// </summary>
    private bool ValidateBiarc(
        BendSegment bend1, BendSegment bend2,
        double startX, double startY,
        double endX, double endY)
    {
        // Validate start point
        if (!_validator.ValidateStartPoint(bend1, startX, startY, out double startError))
        {
            Console.WriteLine($"[TwoBendSolver]     Start validation failed: {startError:F3}µm offset");
            return false;
        }

        // Validate continuity
        if (!_validator.ValidateContinuity(bend1, bend2, out double continuityError))
        {
            Console.WriteLine($"[TwoBendSolver]     Continuity failed: {continuityError:F3}µm gap");
            return false;
        }

        // Validate end point
        if (!_validator.ValidateEndpoint(bend2, endX, endY, out double endError))
        {
            Console.WriteLine($"[TwoBendSolver]     End validation failed: {endError:F3}µm offset");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Gets the list of radii to try, in increasing order.
    /// </summary>
    private List<double> GetRadiiToTry()
    {
        var radii = new List<double>();

        if (_allowedRadii.Count > 0)
        {
            // Use allowed radii in increasing order
            radii.AddRange(_allowedRadii.Where(r => r >= _minBendRadius).OrderBy(r => r));
        }
        else
        {
            // Try min radius, then 1.5x, 2x, 3x multiples
            radii.Add(_minBendRadius);
            radii.Add(_minBendRadius * 1.5);
            radii.Add(_minBendRadius * 2.0);
            radii.Add(_minBendRadius * 3.0);
        }

        return radii;
    }
}
