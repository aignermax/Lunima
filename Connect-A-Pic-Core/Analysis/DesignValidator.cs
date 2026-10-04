using System.Globalization;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using Component = CAP_Core.Components.Core.Component;

namespace CAP_Core.Analysis;

/// <summary>
/// Validates waveguide connections in a design and reports issues
/// such as invalid geometry (bend radius violations), blocked paths,
/// connection×connection waveguide crossings, and overlapping waveguides
/// including frozen group paths.
/// </summary>
public class DesignValidator
{
    private readonly WaveguideOverlapDetector _overlapDetector = new();
    private readonly ConnectionCrossingDetector _crossingDetector = new();
    private readonly WaveguideSpacingDetector _spacingDetector = new();
    private readonly WaveguideMinWidthChecker _minWidthChecker = new();
    private readonly PerConnectionDrcChecker _perConnectionDrcChecker = new();
    private readonly ChipletInterfaceChecker _chipletInterfaceChecker = new();
    private readonly ComponentPdkCompatibilityChecker _pdkCompatibilityChecker = new();
    private readonly ComponentFootprintOverlapChecker _footprintOverlapChecker = new();

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

        issues.AddRange(_crossingDetector.DetectCrossings(connectionList));

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
    /// Validates waveguide connections, detects overlaps with frozen paths, and checks
    /// edge-to-edge waveguide spacing against a process-defined minimum.
    /// </summary>
    /// <param name="connections">Regular waveguide connections to validate.</param>
    /// <param name="groups">ComponentGroups whose frozen internal paths are checked.</param>
    /// <param name="minWaveguideSpacingMicrometers">Minimum required edge-to-edge spacing.</param>
    /// <returns>A list of all design issues found, empty if the design is valid.</returns>
    public List<DesignIssue> Validate(
        IEnumerable<WaveguideConnection> connections,
        IEnumerable<ComponentGroup> groups,
        double minWaveguideSpacingMicrometers)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(groups);

        var connectionList = connections.ToList();
        var issues = Validate(connectionList);
        issues.AddRange(_overlapDetector.DetectOverlaps(connectionList, groups));
        issues.AddRange(_spacingDetector.DetectViolations(connectionList, groups, minWaveguideSpacingMicrometers));
        return issues;
    }

    /// <summary>
    /// Validates waveguide connections and checks every optical pin on the provided
    /// components for a waveguide connection. Pins listed in <paramref name="externalPortPins"/>
    /// are treated as external ports and are not reported as unconnected.
    /// Also flags top-level placed items whose footprints physically overlap.
    /// </summary>
    /// <param name="connections">Regular waveguide connections to validate.</param>
    /// <param name="components">All placed components whose optical pins and footprints are checked.</param>
    /// <param name="externalPortPins">Pins that are external ports and should be skipped. Optional.</param>
    /// <returns>A list of all design issues found, empty if the design is valid.</returns>
    public List<DesignIssue> Validate(
        IEnumerable<WaveguideConnection> connections,
        IEnumerable<Component> components,
        IEnumerable<PhysicalPin>? externalPortPins = null)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(components);

        var connectionList = connections.ToList();
        var issues = Validate(connectionList);
        issues.AddRange(ValidateUnconnectedPins(components, connectionList, externalPortPins));
        issues.AddRange(_footprintOverlapChecker.DetectOverlaps(components));
        return issues;
    }

    /// <summary>
    /// Validates waveguide connections, detects overlaps with frozen paths, and checks
    /// every optical pin on the provided components for a waveguide connection.
    /// Pins listed in <paramref name="externalPortPins"/> are treated as external ports
    /// and are not reported as unconnected. Also flags top-level placed items
    /// (components and groups, one footprint per group) whose placed, rotation-aware
    /// footprint rectangles physically overlap.
    /// </summary>
    /// <param name="connections">Regular waveguide connections to validate.</param>
    /// <param name="groups">ComponentGroups whose frozen internal paths are checked for overlap.</param>
    /// <param name="components">All placed components whose optical pins and footprints are checked.</param>
    /// <param name="externalPortPins">Pins that are external ports and should be skipped. Optional.</param>
    /// <returns>A list of all design issues found, empty if the design is valid.</returns>
    public List<DesignIssue> Validate(
        IEnumerable<WaveguideConnection> connections,
        IEnumerable<ComponentGroup> groups,
        IEnumerable<Component> components,
        IEnumerable<PhysicalPin>? externalPortPins = null)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(components);

        var connectionList = connections.ToList();
        var issues = Validate(connectionList, groups);
        issues.AddRange(ValidateUnconnectedPins(components, connectionList, externalPortPins));
        issues.AddRange(_footprintOverlapChecker.DetectOverlaps(components));
        return issues;
    }

    /// <summary>
    /// Full DRC-lite aggregation: validates waveguide connections, detects overlaps with
    /// frozen paths, checks every optical pin on the provided components for a connection,
    /// flags top-level components/groups whose footprints physically overlap,
    /// (when <paramref name="minWaveguideSpacingMicrometers"/> &gt; 0) checks edge-to-edge
    /// waveguide spacing against the process minimum, and (when
    /// <paramref name="minWaveguideWidthRules"/> are provided) flags waveguides narrower
    /// than the fabrication minimum of their cross-section, and flags cross-chiplet
    /// edge-coupler links whose facets do not face each other, are laterally offset,
    /// sit off the chiplet edge, or stand too far apart (facet-gap divergence loss,
    /// issues #1219/#1238 — chiplet membership resolves from the pins' parent groups,
    /// so the checker needs no extra input). Each rule contributes its findings once.
    /// </summary>
    /// <param name="connections">Regular waveguide connections to validate.</param>
    /// <param name="groups">ComponentGroups whose frozen internal paths are checked for overlap.</param>
    /// <param name="components">All placed components whose optical pins are checked.</param>
    /// <param name="externalPortPins">Pins that are external ports and should be skipped.</param>
    /// <param name="wavelengthNm">Simulation wavelength the chiplet facet-gap loss is evaluated at.</param>
    /// <param name="minWaveguideSpacingMicrometers">
    /// Minimum required edge-to-edge spacing; ≤0 disables the spacing check. When a
    /// per-connection provider is wired this value still governs frozen group paths,
    /// which carry no pins to resolve an owning process from.
    /// </param>
    /// <param name="minWaveguideWidthRules">
    /// Per-cross-section minimum feature widths of the active process; null/empty disables
    /// the min-width check (the PDK declares no <c>minWidthUm</c>). Ignored for
    /// connections when <paramref name="connectionDrcRuleProvider"/> is wired.
    /// </param>
    /// <param name="connectionDrcRuleProvider">
    /// Optional per-connection rule-set resolver (issue #936): when wired, EACH
    /// connection's width and spacing limits come from its own endpoint PDKs' processes
    /// instead of the design-wide values above, so a multi-process (e.g. two-chiplet)
    /// canvas checks every chiplet against its own foundry limits — including in
    /// Playground, where no process lock exists at all. A null return means "no PDK
    /// opinion" (built-ins, PDK-less components): the design-wide values above govern,
    /// the same fallback rule as the router's per-connection bend floor (#937). A
    /// non-null but empty rule set means the endpoint PDKs are known and declare no
    /// minimum — the connection stays silent (no invented values, #926).
    /// </param>
    /// <returns>A list of all design issues found, empty if the design is valid.</returns>
    public List<DesignIssue> Validate(
        IEnumerable<WaveguideConnection> connections,
        IEnumerable<ComponentGroup> groups,
        IEnumerable<Component> components,
        IEnumerable<PhysicalPin>? externalPortPins,
        double wavelengthNm,
        double minWaveguideSpacingMicrometers = 0,
        IReadOnlyList<WaveguideMinWidthRule>? minWaveguideWidthRules = null,
        Func<WaveguideConnection, ConnectionDrcRules?>? connectionDrcRuleProvider = null)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(components);

        var connectionList = connections.ToList();
        var issues = Validate(connectionList, groups, components, externalPortPins);
        issues.AddRange(_chipletInterfaceChecker.Check(connectionList, wavelengthNm));

        if (connectionDrcRuleProvider is not null)
        {
            issues.AddRange(_perConnectionDrcChecker.Check(
                connectionList, groups, minWaveguideSpacingMicrometers,
                minWaveguideWidthRules, connectionDrcRuleProvider));
            return issues;
        }

        if (minWaveguideSpacingMicrometers > 0)
        {
            issues.AddRange(_spacingDetector.DetectViolations(
                connectionList, groups, minWaveguideSpacingMicrometers));
        }
        if (minWaveguideWidthRules is { Count: > 0 })
        {
            issues.AddRange(_minWidthChecker.CheckConnections(connectionList, minWaveguideWidthRules));
        }
        return issues;
    }

    /// <summary>
    /// Checks every optical pin on the provided components and reports any that have no
    /// waveguide connection and are not listed as external ports.
    /// </summary>
    /// <param name="components">All placed components whose optical pins are checked.</param>
    /// <param name="connections">Regular waveguide connections; endpoint pins are considered connected.</param>
    /// <param name="externalPortPins">Pins that are external ports and should be skipped. Optional.</param>
    /// <returns>A list of unconnected-pin issues, empty when every optical pin is connected or external.</returns>
    public List<DesignIssue> ValidateUnconnectedPins(
        IEnumerable<Component> components,
        IEnumerable<WaveguideConnection> connections,
        IEnumerable<PhysicalPin>? externalPortPins = null)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(connections);

        var connectedPins = new HashSet<PhysicalPin>(
            connections.SelectMany(c => new[] { c.StartPin, c.EndPin }).OfType<PhysicalPin>());
        var externalPortPinSet = new HashSet<PhysicalPin>(externalPortPins ?? Array.Empty<PhysicalPin>());

        var issues = new List<DesignIssue>();

        foreach (var component in components)
        {
            foreach (var pin in component.PhysicalPins)
            {
                if (pin.MatterType != MatterType.Light)
                    continue;
                if (connectedPins.Contains(pin))
                    continue;
                if (externalPortPinSet.Contains(pin))
                    continue;

                var (x, y) = pin.GetAbsolutePosition();
                issues.Add(new DesignIssue(
                    DesignIssueType.UnconnectedPin,
                    connection: null,
                    x,
                    y,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Unconnected pin: {FormatPinName(pin)} at ({x}, {y})"),
                    localizationKey: "DesignChecks.UnconnectedPin",
                    localizationArgs: new object[] { FormatPinName(pin), x, y }));
            }
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
        var startName = FormatPinName(connection.StartPin);
        var endName = FormatPinName(connection.EndPin);

        void Add(DesignIssueType type, string description, string localizationKey) =>
            issues.Add(new DesignIssue(
                type, connection, midX, midY, description,
                localizationKey: localizationKey,
                localizationArgs: new object[] { startName, endName }));

        if (connection.RoutedPath?.IsInvalidGeometry == true)
            Add(DesignIssueType.InvalidGeometry,
                $"Bend radius violation: {startName} to {endName}",
                "DesignChecks.InvalidGeometry");

        if (connection.IsBlockedFallback)
            Add(DesignIssueType.BlockedPath,
                FormatBlockedPathMessage(connection, startName, endName),
                BlockedPathLocalizationKey(connection));

        if (connection.RoutedPath?.ViolatesProcessMinBendRadius == true)
            Add(DesignIssueType.BendRadiusBelowProcessMinimum,
                $"Bend radius below process minimum: {startName} to {endName}",
                "DesignChecks.BendRadiusBelowProcessMinimum");

        if (connection.RoutedPath?.PassesThroughComponent == true)
            Add(DesignIssueType.StyledRouteThroughComponent,
                $"Styled route passes through a component: {startName} to {endName}",
                "DesignChecks.StyledRouteThroughComponent");

        CheckPinMismatch(connection, issues);
    }

    /// <summary>
    /// Builds the blocked-path message from the router's failure classification: a pin
    /// sealed in by a component footprint needs the component moved (re-routing cannot
    /// help), while contention between wires may be fixed by re-routing or reordering.
    /// </summary>
    private static string FormatBlockedPathMessage(
        WaveguideConnection connection, string startName, string endName)
    {
        return connection.FailureReason switch
        {
            RoutingFailureReason.EndpointBlocked =>
                $"Blocked path: {startName} to {endName} — a pin is sealed in by a component footprint; move the component (re-routing cannot fix this)",
            RoutingFailureReason.Contention =>
                $"Blocked path: {startName} to {endName} — no free lane; other waveguides occupy the corridor",
            _ => $"Blocked path: {startName} to {endName}",
        };
    }

    /// <summary>
    /// Localization key for the blocked-path wording matching
    /// <see cref="FormatBlockedPathMessage"/>'s failure-reason classification.
    /// </summary>
    private static string BlockedPathLocalizationKey(WaveguideConnection connection)
    {
        return connection.FailureReason switch
        {
            RoutingFailureReason.EndpointBlocked => "DesignChecks.BlockedPath.EndpointBlocked",
            RoutingFailureReason.Contention => "DesignChecks.BlockedPath.Contention",
            _ => "DesignChecks.BlockedPath",
        };
    }

    /// <summary>
    /// Checks whether the two endpoint pins of a connection have matching PDK-driven
    /// waveguide widths and layers. A mismatch produces a <see cref="DesignIssueType.PinMismatch"/>.
    /// </summary>
    private static void CheckPinMismatch(
        WaveguideConnection connection,
        List<DesignIssue> issues)
    {
        if (connection.StartPin == null || connection.EndPin == null)
            return;

        var (midX, midY) = CalculateMidpoint(connection);

        var startWidth = connection.StartPin.WaveguideWidthMicrometers;
        var endWidth = connection.EndPin.WaveguideWidthMicrometers;
        if (startWidth.HasValue && endWidth.HasValue && startWidth.Value != endWidth.Value)
        {
            var startName = FormatPinName(connection.StartPin);
            var endName = FormatPinName(connection.EndPin);
            issues.Add(new DesignIssue(
                DesignIssueType.PinMismatch,
                connection,
                midX,
                midY,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Pin width mismatch: {startName} ({startWidth.Value} µm) vs {endName} ({endWidth.Value} µm)"),
                localizationKey: "DesignChecks.PinWidthMismatch",
                localizationArgs: new object[] { startName, startWidth.Value, endName, endWidth.Value }));
        }

        var startLayer = connection.StartPin.Layer;
        var endLayer = connection.EndPin.Layer;
        if (startLayer.HasValue && endLayer.HasValue && startLayer.Value != endLayer.Value)
        {
            var startName = FormatPinName(connection.StartPin);
            var endName = FormatPinName(connection.EndPin);
            issues.Add(new DesignIssue(
                DesignIssueType.PinMismatch,
                connection,
                midX,
                midY,
                $"Pin layer mismatch: {startName} (layer {startLayer.Value}) vs {endName} (layer {endLayer.Value})",
                localizationKey: "DesignChecks.PinLayerMismatch",
                localizationArgs: new object[] { startName, startLayer.Value, endName, endLayer.Value }));
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
                // Invariant culture: '5.0' is part of the test contract and the user-facing
                // unit format. Without this, de-DE / fr-FR machines render '5,0'.
                description: string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{name}' is outside chip bounds ({wMm:F1} × {hMm:F1} mm)"),
                localizationKey: "DesignChecks.OutOfBounds",
                localizationArgs: new object[] { name, wMm, hMm }));
        }

        return issues;
    }

    /// <summary>
    /// Checks whether any placed components belong to a PDK that no longer matches the design's
    /// active fabrication process. Delegates to <see cref="ComponentPdkCompatibilityChecker"/>.
    /// </summary>
    public List<DesignIssue> ValidateComponentPdkCompatibility(
        IEnumerable<Component> components,
        IReadOnlyDictionary<Component, string?> pdkSourceByComponent,
        IReadOnlyCollection<string> processAgnosticPdkNames,
        IReadOnlyCollection<string> enabledPdkNames,
        bool processLockActive = true)
    {
        return _pdkCompatibilityChecker.Check(
            components, pdkSourceByComponent,
            processAgnosticPdkNames, enabledPdkNames, processLockActive);
    }

    /// <summary>
    /// Formats a pin name for display as "ComponentId.PinName".
    /// </summary>
    private static string FormatPinName(PhysicalPin pin)
    {
        return $"{pin.ParentComponent.Identifier}.{pin.Name}";
    }
}
