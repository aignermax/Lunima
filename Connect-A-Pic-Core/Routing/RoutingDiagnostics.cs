using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing;

/// <summary>
/// Validates routed paths against physical constraints and reports diagnostics.
/// Checks bend radii, segment continuity, entry angles, and obstacle crossings.
/// </summary>
public class RoutingDiagnostics
{
    /// <summary>
    /// Minimum allowed bend radius in micrometers.
    /// </summary>
    public double MinBendRadiusMicrometers { get; }

    /// <summary>
    /// Tolerance for segment continuity checks (micrometers).
    /// </summary>
    public const double ContinuityToleranceMicrometers = 0.5;

    /// <summary>
    /// Tolerance for angle comparisons (degrees).
    /// </summary>
    public const double AngleToleranceDegrees = 15.0;

    /// <summary>
    /// Maximum allowed sweep angle for a single bend (degrees).
    /// </summary>
    public const double MaxSweepAngleDegrees = 135.0;

    public RoutingDiagnostics(double minBendRadiusMicrometers)
    {
        MinBendRadiusMicrometers = minBendRadiusMicrometers;
    }

    /// <summary>
    /// Validates an entire routed path and returns a diagnostic report.
    /// </summary>
    /// <param name="path">The routed path to validate</param>
    /// <returns>Diagnostic report with all detected issues</returns>
    public RoutingDiagnosticReport Validate(RoutedPath path)
    {
        var report = new RoutingDiagnosticReport();

        if (path.Segments.Count == 0)
        {
            report.Issues.Add(new RoutingIssue(
                RoutingIssueSeverity.Error,
                "Path has no segments"));
            return report;
        }

        ValidateBendRadii(path, report);
        ValidateSegmentContinuity(path, report);
        ValidateSweepAngles(path, report);
        ValidateSegmentLengths(path, report);

        if (path.IsBlockedFallback)
        {
            report.Issues.Add(new RoutingIssue(
                RoutingIssueSeverity.Warning,
                "Path uses blocked fallback routing (may cross obstacles)"));
        }

        if (path.IsInvalidGeometry)
        {
            report.Issues.Add(new RoutingIssue(
                RoutingIssueSeverity.Error,
                "Path has invalid geometry (physical constraint violation)"));
        }

        return report;
    }

    /// <summary>
    /// Validates that a path reaches from start to end pins correctly.
    /// </summary>
    /// <param name="path">The routed path</param>
    /// <param name="startX">Expected start X</param>
    /// <param name="startY">Expected start Y</param>
    /// <param name="endX">Expected end X</param>
    /// <param name="endY">Expected end Y</param>
    /// <param name="endEntryAngle">Expected end entry angle (degrees)</param>
    /// <returns>Diagnostic report</returns>
    public RoutingDiagnosticReport ValidateEndpoints(
        RoutedPath path,
        double startX, double startY,
        double endX, double endY,
        double endEntryAngle)
    {
        var report = Validate(path);
        if (path.Segments.Count == 0) return report;

        var first = path.Segments[0];
        double startDist = Distance(first.StartPoint.X, first.StartPoint.Y, startX, startY);
        if (startDist > ContinuityToleranceMicrometers * 2)
        {
            report.Issues.Add(new RoutingIssue(
                RoutingIssueSeverity.Warning,
                $"Start point gap: {startDist:F2}µm from pin"));
        }

        var last = path.Segments[^1];
        double endDist = Distance(last.EndPoint.X, last.EndPoint.Y, endX, endY);
        if (endDist > ContinuityToleranceMicrometers * 2)
        {
            report.Issues.Add(new RoutingIssue(
                RoutingIssueSeverity.Warning,
                $"End point gap: {endDist:F2}µm from pin"));
        }

        double entryAngleDiff = Math.Abs(
            AngleUtilities.NormalizeAngle(last.EndAngleDegrees - endEntryAngle));
        if (entryAngleDiff > AngleToleranceDegrees)
        {
            report.Issues.Add(new RoutingIssue(
                RoutingIssueSeverity.Warning,
                $"Entry angle mismatch: {entryAngleDiff:F1}° (expected {endEntryAngle:F0}°, got {last.EndAngleDegrees:F0}°)"));
        }

        return report;
    }

    private void ValidateBendRadii(RoutedPath path, RoutingDiagnosticReport report)
    {
        for (int i = 0; i < path.Segments.Count; i++)
        {
            if (path.Segments[i] is BendSegment bend)
            {
                if (bend.RadiusMicrometers < MinBendRadiusMicrometers - 0.1)
                {
                    report.Issues.Add(new RoutingIssue(
                        RoutingIssueSeverity.Error,
                        $"Segment {i}: bend radius {bend.RadiusMicrometers:F1}µm < minimum {MinBendRadiusMicrometers:F1}µm"));
                }
            }
        }
    }

    private static void ValidateSegmentContinuity(RoutedPath path, RoutingDiagnosticReport report)
    {
        for (int i = 1; i < path.Segments.Count; i++)
        {
            var prev = path.Segments[i - 1];
            var curr = path.Segments[i];
            double gap = Distance(
                prev.EndPoint.X, prev.EndPoint.Y,
                curr.StartPoint.X, curr.StartPoint.Y);

            if (gap > ContinuityToleranceMicrometers)
            {
                report.Issues.Add(new RoutingIssue(
                    RoutingIssueSeverity.Error,
                    $"Segments {i - 1}→{i}: gap of {gap:F2}µm"));
            }
        }
    }

    private static void ValidateSweepAngles(RoutedPath path, RoutingDiagnosticReport report)
    {
        for (int i = 0; i < path.Segments.Count; i++)
        {
            if (path.Segments[i] is BendSegment bend)
            {
                if (Math.Abs(bend.SweepAngleDegrees) > MaxSweepAngleDegrees)
                {
                    report.Issues.Add(new RoutingIssue(
                        RoutingIssueSeverity.Warning,
                        $"Segment {i}: excessive sweep angle {bend.SweepAngleDegrees:F1}°"));
                }
            }
        }
    }

    private static void ValidateSegmentLengths(RoutedPath path, RoutingDiagnosticReport report)
    {
        for (int i = 0; i < path.Segments.Count; i++)
        {
            if (path.Segments[i].LengthMicrometers < 0)
            {
                report.Issues.Add(new RoutingIssue(
                    RoutingIssueSeverity.Error,
                    $"Segment {i}: negative length {path.Segments[i].LengthMicrometers:F2}µm"));
            }
        }
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        return Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
    }
}

/// <summary>
/// Report containing all routing diagnostic issues for a path.
/// </summary>
public class RoutingDiagnosticReport
{
    /// <summary>
    /// All detected issues.
    /// </summary>
    public List<RoutingIssue> Issues { get; } = new();

    /// <summary>
    /// Whether the path passes all validation checks.
    /// </summary>
    public bool IsValid => !Issues.Any(i => i.Severity == RoutingIssueSeverity.Error);

    /// <summary>
    /// Number of errors (physical constraint violations).
    /// </summary>
    public int ErrorCount => Issues.Count(i => i.Severity == RoutingIssueSeverity.Error);

    /// <summary>
    /// Number of warnings (non-ideal but valid geometry).
    /// </summary>
    public int WarningCount => Issues.Count(i => i.Severity == RoutingIssueSeverity.Warning);

    /// <summary>
    /// Returns a formatted summary of all issues.
    /// </summary>
    public string FormatSummary()
    {
        if (Issues.Count == 0) return "Path OK — no issues detected";
        return string.Join("\n", Issues.Select(i => $"[{i.Severity}] {i.Message}"));
    }
}

/// <summary>
/// A single routing diagnostic issue.
/// </summary>
public class RoutingIssue
{
    /// <summary>
    /// Severity of the issue.
    /// </summary>
    public RoutingIssueSeverity Severity { get; }

    /// <summary>
    /// Description of the issue.
    /// </summary>
    public string Message { get; }

    public RoutingIssue(RoutingIssueSeverity severity, string message)
    {
        Severity = severity;
        Message = message;
    }
}

/// <summary>
/// Severity level for routing issues.
/// </summary>
public enum RoutingIssueSeverity
{
    /// <summary>
    /// Non-ideal but valid geometry (e.g., excessive sweep angle).
    /// </summary>
    Warning,

    /// <summary>
    /// Physical constraint violation (e.g., bend radius too small).
    /// </summary>
    Error
}
