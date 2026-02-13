namespace CAP_Core.Routing.Analysis;

/// <summary>
/// A single diagnostic finding from routing complexity analysis.
/// </summary>
public class RoutingDiagnosticEntry
{
    /// <summary>
    /// The severity of this diagnostic finding.
    /// </summary>
    public RoutingDiagnosticSeverity Severity { get; }

    /// <summary>
    /// A human-readable description of the finding.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Creates a new diagnostic entry.
    /// </summary>
    /// <param name="severity">The severity level.</param>
    /// <param name="message">Description of the finding.</param>
    public RoutingDiagnosticEntry(RoutingDiagnosticSeverity severity, string message)
    {
        Severity = severity;
        Message = message;
    }

    /// <inheritdoc />
    public override string ToString() => $"[{Severity}] {Message}";
}
