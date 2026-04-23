using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using Component = CAP_Core.Components.Core.Component;

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
    /// Checks whether any components exceed the specified chip footprint and returns issues
    /// for any that are fully or partially outside the boundary (0,0) to
    /// (<paramref name="chipWidthMicrometers"/>, <paramref name="chipHeightMicrometers"/>).
    /// Components outside bounds are flagged with <see cref="DesignIssueType.OutOfBounds"/>;
    /// they are never moved or deleted.
    /// </summary>
    /// <param name="components">All placed components to check.</param>
    /// <param name="chipWidthMicrometers">Chip boundary width in micrometers.</param>
    /// <param name="chipHeightMicrometers">Chip boundary height in micrometers.</param>
    /// <returns>List of out-of-bounds issues, empty when all components are within bounds.</returns>
    public List<DesignIssue> ValidateComponentBounds(
        IEnumerable<Component> components,
        double chipWidthMicrometers,
        double chipHeightMicrometers)
    {
        ArgumentNullException.ThrowIfNull(components);

        var issues = new List<DesignIssue>();

        foreach (var component in components)
        {
            double right  = component.PhysicalX + component.WidthMicrometers;
            double bottom = component.PhysicalY + component.HeightMicrometers;

            bool outOfBounds = component.PhysicalX < 0
                || component.PhysicalY < 0
                || right  > chipWidthMicrometers
                || bottom > chipHeightMicrometers;

            if (!outOfBounds) continue;

            double centerX = component.PhysicalX + component.WidthMicrometers / 2;
            double centerY = component.PhysicalY + component.HeightMicrometers / 2;
            double wMm = chipWidthMicrometers  / 1000.0;
            double hMm = chipHeightMicrometers / 1000.0;
            string name = component.HumanReadableName ?? component.Identifier;

            issues.Add(new DesignIssue(
                DesignIssueType.OutOfBounds,
                connection: null,
                x: centerX,
                y: centerY,
                description: $"'{name}' is outside chip bounds ({wMm:F1} × {hMm:F1} mm)"));
        }

        return issues;
    }

    /// <summary>
    /// Formats a pin name for display as "ComponentId.PinName".
    /// </summary>
    private static string FormatPinName(PhysicalPin pin)
    {
        return $"{pin.ParentComponent.Identifier}.{pin.Name}";
    }
}
