namespace CAP_Core.Export;

/// <summary>
/// Configuration options for Verilog-A export.
/// </summary>
public class VerilogAExportOptions
{
    /// <summary>
    /// Wavelength in nanometers for S-parameter extraction.
    /// Default: 1550nm (telecom C-band).
    /// </summary>
    public int WavelengthNm { get; set; } = 1550;

    /// <summary>
    /// Name of the top-level Verilog-A circuit module.
    /// </summary>
    public string CircuitName { get; set; } = "PhotonicCircuit";

    /// <summary>
    /// Whether to include a SPICE test bench file.
    /// </summary>
    public bool IncludeTestBench { get; set; } = true;
}
