namespace CAP_Core.Export;

/// <summary>
/// Result of a Verilog-A export operation.
/// Contains all generated file contents keyed by filename.
/// </summary>
public class VerilogAExportResult
{
    /// <summary>
    /// Whether the export succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if export failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Component library files: filename (.va) → Verilog-A module content.
    /// </summary>
    public Dictionary<string, string> ComponentFiles { get; init; } = new();

    /// <summary>
    /// Top-level netlist Verilog-A content.
    /// </summary>
    public string TopLevelNetlist { get; init; } = string.Empty;

    /// <summary>
    /// SPICE test bench content (.sp file).
    /// </summary>
    public string SpiceTestBench { get; init; } = string.Empty;

    /// <summary>
    /// Name of the top-level circuit module.
    /// </summary>
    public string CircuitName { get; init; } = string.Empty;

    /// <summary>
    /// Total number of files generated (component files + netlist + optional test bench).
    /// </summary>
    public int TotalFileCount =>
        ComponentFiles.Count + (string.IsNullOrEmpty(TopLevelNetlist) ? 0 : 1) +
        (string.IsNullOrEmpty(SpiceTestBench) ? 0 : 1);

    /// <summary>
    /// Creates a failed export result with an error message.
    /// </summary>
    public static VerilogAExportResult Failure(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}
