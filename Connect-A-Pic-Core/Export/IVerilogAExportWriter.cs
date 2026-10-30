namespace CAP_Core.Export;

/// <summary>Persists one generated Verilog-A circuit in its component/netlist/testbench layout.</summary>
public interface IVerilogAExportWriter
{
    Task<string?> WriteAsync(VerilogAExportResult result, string outputDirectory);
}
