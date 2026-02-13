namespace CAP_Core.Routing.Analysis;

/// <summary>
/// Severity level for a routing complexity diagnostic entry.
/// </summary>
public enum RoutingDiagnosticSeverity
{
    /// <summary>
    /// Informational finding that does not indicate a problem.
    /// </summary>
    Info,

    /// <summary>
    /// A warning indicating the design may be hard to realize physically.
    /// </summary>
    Warning,

    /// <summary>
    /// A critical finding indicating the design is likely unrealizable.
    /// </summary>
    Error
}
