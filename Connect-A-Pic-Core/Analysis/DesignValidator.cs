using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;

namespace CAP_Core.Analysis;

/// <summary>
/// Validates waveguide connections in a design and reports issues
/// such as invalid geometry (bend radius violations), blocked paths,
/// and overlapping waveguides including frozen group paths.
/// </summary>
public class DesignValidator
{
    private readonly WaveguideOverlapDetector _overlapDetector = new();

    /// <summary>
    /// Validates all provided waveguide connections and returns any issues found.
    /// Does not include frozen path overlap detection (use the overload with groups for that).
    /// </summary>
    /// <param name="connections">The connections to validate.</param>
    /// <returns>A list of design issues, empty if all connections are valid.</returns>
    public List<DesignIssue> Validate(IEnumerable<WaveguideConnection> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);

        var connectionList = connections.ToList();
        var issues = new List<DesignIssue>();

        foreach (var connection in connectionList)
        {
            CheckConnection(connection, issues);
        }

        return issues;
    }

    /// <summary>
    /// Validates waveguide connections and detects overlaps with frozen paths in ComponentGroups.
    /// </summary>
    /// <param name="connections">Regular waveguide connections to validate.</param>
    /// <param name="groups">ComponentGroups whose frozen internal paths are checked for overlap.</param>
    /// <returns>A list of all design issues found, empty if the design is valid.</returns>
    public List<DesignIssue> Validate(
        IEnumerable<WaveguideConnection> connections,
        IEnumerable<ComponentGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(groups);

        var connectionList = connections.ToList();
        var issues = Validate(connectionList);
        issues.AddRange(_overlapDetector.DetectOverlaps(connectionList, groups));
        return issues;
    }

    /// <summary>
    /// Checks a single connection for issues and adds them to the list.
    /// </summary>
    private static void CheckConnection(
        WaveguideConnection connection,
        List<DesignIssue> issues)
    {
        var (midX, midY) = CalculateMidpoint(connection);

        if (connection.RoutedPath?.IsInvalidGeometry == true)
        {
            var startName = FormatPinName(connection.StartPin);
            var endName = FormatPinName(connection.EndPin);
            issues.Add(new DesignIssue(
                DesignIssueType.InvalidGeometry,
                connection,
                midX,
                midY,
                $"Bend radius violation: {startName} to {endName}"));
        }

        if (connection.IsBlockedFallback)
        {
            var startName = FormatPinName(connection.StartPin);
            var endName = FormatPinName(connection.EndPin);
            issues.Add(new DesignIssue(
                DesignIssueType.BlockedPath,
                connection,
                midX,
                midY,
                $"Blocked path: {startName} to {endName}"));
        }
    }

    /// <summary>
    /// Calculates the midpoint between a connection's start and end pins.
    /// </summary>
    private static (double x, double y) CalculateMidpoint(
        WaveguideConnection connection)
    {
        var (startX, startY) = connection.StartPin.GetAbsolutePosition();
        var (endX, endY) = connection.EndPin.GetAbsolutePosition();
        return ((startX + endX) / 2, (startY + endY) / 2);
    }

    /// <summary>
    /// Formats a pin name for display as "ComponentId.PinName".
    /// </summary>
    private static string FormatPinName(PhysicalPin pin)
    {
        return $"{pin.ParentComponent.Identifier}.{pin.Name}";
    }
}
