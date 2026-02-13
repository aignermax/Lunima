namespace CAP_Core.Routing.Analysis;

/// <summary>
/// Contains the results of a routing complexity analysis for a design.
/// Provides metrics about bend count, congestion, and path lengths.
/// </summary>
public class RoutingComplexityResult
{
    private readonly List<RoutingDiagnosticEntry> _diagnostics = new();

    /// <summary>
    /// Total number of equivalent 90-degree bends across all connections.
    /// </summary>
    public double TotalBendCount { get; init; }

    /// <summary>
    /// Average number of equivalent 90-degree bends per connection.
    /// Zero if there are no connections.
    /// </summary>
    public double AverageBendsPerConnection { get; init; }

    /// <summary>
    /// Routing congestion score as waveguide-occupied cell fraction (0.0 to 1.0).
    /// Higher values indicate denser waveguide packing.
    /// </summary>
    public double CongestionScore { get; init; }

    /// <summary>
    /// Length of the longest routed path in micrometers.
    /// Zero if there are no connections.
    /// </summary>
    public double LongestPathMicrometers { get; init; }

    /// <summary>
    /// Total number of routed connections analyzed.
    /// </summary>
    public int ConnectionCount { get; init; }

    /// <summary>
    /// Number of connections that use fallback (blocked) paths.
    /// </summary>
    public int BlockedFallbackCount { get; init; }

    /// <summary>
    /// All diagnostic entries (warnings and errors).
    /// </summary>
    public IReadOnlyList<RoutingDiagnosticEntry> Diagnostics => _diagnostics;

    /// <summary>
    /// True if there are any warning-level diagnostics.
    /// </summary>
    public bool HasWarnings =>
        _diagnostics.Any(d => d.Severity == RoutingDiagnosticSeverity.Warning);

    /// <summary>
    /// True if there are any error-level diagnostics.
    /// </summary>
    public bool HasErrors =>
        _diagnostics.Any(d => d.Severity == RoutingDiagnosticSeverity.Error);

    /// <summary>
    /// All warning-level entries.
    /// </summary>
    public IEnumerable<RoutingDiagnosticEntry> Warnings =>
        _diagnostics.Where(d => d.Severity == RoutingDiagnosticSeverity.Warning);

    /// <summary>
    /// All error-level entries.
    /// </summary>
    public IEnumerable<RoutingDiagnosticEntry> Errors =>
        _diagnostics.Where(d => d.Severity == RoutingDiagnosticSeverity.Error);

    /// <summary>
    /// Adds a diagnostic entry to the result.
    /// </summary>
    /// <param name="entry">The entry to add.</param>
    public void AddDiagnostic(RoutingDiagnosticEntry entry)
    {
        _diagnostics.Add(entry);
    }

    /// <summary>
    /// Adds a warning diagnostic.
    /// </summary>
    /// <param name="message">The warning message.</param>
    public void AddWarning(string message)
    {
        _diagnostics.Add(new RoutingDiagnosticEntry(
            RoutingDiagnosticSeverity.Warning, message));
    }

    /// <summary>
    /// Adds an error diagnostic.
    /// </summary>
    /// <param name="message">The error message.</param>
    public void AddError(string message)
    {
        _diagnostics.Add(new RoutingDiagnosticEntry(
            RoutingDiagnosticSeverity.Error, message));
    }

    /// <summary>
    /// Returns a formatted summary of the routing complexity metrics.
    /// </summary>
    public string GetSummary()
    {
        return $"Connections: {ConnectionCount}, " +
               $"Total bends: {TotalBendCount:F1}, " +
               $"Avg bends/conn: {AverageBendsPerConnection:F2}, " +
               $"Congestion: {CongestionScore:P1}, " +
               $"Longest path: {LongestPathMicrometers:F0} µm";
    }
}
