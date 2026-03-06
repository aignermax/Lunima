using CAP_Core.Components;

namespace CAP_Core.Analysis;

/// <summary>
/// Validates waveguide connections in a design and reports issues
/// such as invalid geometry (bend radius violations) and blocked paths.
/// </summary>
public class DesignValidator
{
    /// <summary>
    /// Validates all provided waveguide connections and returns any issues found.
    /// </summary>
    /// <param name="connections">The connections to validate.</param>
    /// <returns>A list of design issues, empty if all connections are valid.</returns>
    public List<DesignIssue> Validate(IEnumerable<WaveguideConnection> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);

        var issues = new List<DesignIssue>();

        foreach (var connection in connections)
        {
            CheckConnection(connection, issues);
        }

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
