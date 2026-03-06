using CAP_Core.Components;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;

namespace CAP_Core.Routing;

/// <summary>
/// Runs routing on a deterministic layout test definition.
/// Creates components and pins from the layout, initializes the router,
/// routes all connections, and returns diagnostic results.
/// </summary>
public class LayoutTestRunner
{
    /// <summary>
    /// Result of running a layout test.
    /// </summary>
    public class LayoutTestResult
    {
        /// <summary>
        /// Whether all connections were routed successfully.
        /// </summary>
        public bool AllRoutesSucceeded { get; set; }

        /// <summary>
        /// Results for each connection.
        /// </summary>
        public List<ConnectionResult> ConnectionResults { get; } = new();

        /// <summary>
        /// Number of connections that produced valid paths.
        /// </summary>
        public int SuccessCount => ConnectionResults.Count(r => r.IsSuccess);

        /// <summary>
        /// Total number of connections attempted.
        /// </summary>
        public int TotalCount => ConnectionResults.Count;
    }

    /// <summary>
    /// Result for a single connection.
    /// </summary>
    public class ConnectionResult
    {
        /// <summary>
        /// Connection description (from → to).
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Whether routing succeeded with valid geometry.
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// The routed path (may be null if routing failed completely).
        /// </summary>
        public RoutedPath? Path { get; set; }

        /// <summary>
        /// Diagnostic report for the path.
        /// </summary>
        public RoutingDiagnosticReport? Diagnostics { get; set; }
    }

    /// <summary>
    /// Runs routing on the given layout definition.
    /// </summary>
    /// <param name="layout">The layout to test</param>
    /// <returns>Test results with diagnostics</returns>
    public static LayoutTestResult Run(LayoutTestDefinition layout)
    {
        var result = new LayoutTestResult();
        var components = CreateComponents(layout);
        var router = CreateRouter(layout, components);
        var diagnostics = new RoutingDiagnostics(layout.MinBendRadiusMicrometers);

        foreach (var conn in layout.Connections)
        {
            var connResult = RouteConnection(
                layout, conn, components, router, diagnostics);
            result.ConnectionResults.Add(connResult);
        }

        result.AllRoutesSucceeded = result.ConnectionResults.All(r => r.IsSuccess);
        return result;
    }

    private static List<Component> CreateComponents(LayoutTestDefinition layout)
    {
        var components = new List<Component>();
        foreach (var compDef in layout.Components)
        {
            var parts = new Part[1, 1];
            parts[0, 0] = new Part(new List<Pin>());

            var component = new Component(
                new Dictionary<int, SMatrix>(),
                new List<Slider>(),
                compDef.Type, "", parts, 0,
                $"{compDef.Type}_{compDef.X}_{compDef.Y}",
                DiscreteRotation.R0);

            component.WidthMicrometers = compDef.Width;
            component.HeightMicrometers = compDef.Height;
            component.PhysicalX = compDef.X;
            component.PhysicalY = compDef.Y;

            foreach (var pinDef in compDef.Pins)
            {
                component.PhysicalPins.Add(new PhysicalPin
                {
                    Name = pinDef.Name,
                    OffsetXMicrometers = pinDef.OffsetX,
                    OffsetYMicrometers = pinDef.OffsetY,
                    AngleDegrees = pinDef.AngleDegrees,
                    ParentComponent = component
                });
            }

            components.Add(component);
        }
        return components;
    }

    private static WaveguideRouter CreateRouter(
        LayoutTestDefinition layout, List<Component> components)
    {
        var router = new WaveguideRouter
        {
            MinBendRadiusMicrometers = layout.MinBendRadiusMicrometers,
        };

        // Calculate grid bounds with generous padding
        double padding = 100;
        double minX = components.Min(c => c.PhysicalX) - padding;
        double minY = components.Min(c => c.PhysicalY) - padding;
        double maxX = components.Max(c => c.PhysicalX + c.WidthMicrometers) + padding;
        double maxY = components.Max(c => c.PhysicalY + c.HeightMicrometers) + padding;

        router.InitializePathfindingGrid(minX, minY, maxX, maxY, components);
        return router;
    }

    private static ConnectionResult RouteConnection(
        LayoutTestDefinition layout,
        LayoutConnection conn,
        List<Component> components,
        WaveguideRouter router,
        RoutingDiagnostics diagnostics)
    {
        var fromComp = components[conn.FromComponentIndex];
        var toComp = components[conn.ToComponentIndex];

        var fromPin = fromComp.PhysicalPins.FirstOrDefault(p => p.Name == conn.FromPin);
        var toPin = toComp.PhysicalPins.FirstOrDefault(p => p.Name == conn.ToPin);

        var desc = $"{fromComp.Identifier}.{conn.FromPin} → {toComp.Identifier}.{conn.ToPin}";

        if (fromPin == null || toPin == null)
        {
            return new ConnectionResult
            {
                Description = desc,
                IsSuccess = false,
                Diagnostics = CreatePinNotFoundReport(fromPin, toPin, conn),
            };
        }

        var path = router.Route(fromPin, toPin);
        var report = diagnostics.Validate(path);

        return new ConnectionResult
        {
            Description = desc,
            IsSuccess = path.IsValid && !path.IsBlockedFallback && !path.IsInvalidGeometry,
            Path = path,
            Diagnostics = report,
        };
    }

    private static RoutingDiagnosticReport CreatePinNotFoundReport(
        PhysicalPin? fromPin, PhysicalPin? toPin, LayoutConnection conn)
    {
        var report = new RoutingDiagnosticReport();
        if (fromPin == null)
            report.Issues.Add(new RoutingIssue(RoutingIssueSeverity.Error,
                $"Start pin '{conn.FromPin}' not found"));
        if (toPin == null)
            report.Issues.Add(new RoutingIssue(RoutingIssueSeverity.Error,
                $"End pin '{conn.ToPin}' not found"));
        return report;
    }
}
