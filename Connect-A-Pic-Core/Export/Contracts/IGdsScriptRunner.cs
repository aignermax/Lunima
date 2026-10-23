namespace CAP_Core.Export.Contracts;

/// <summary>Executes an exported script and reports the generated GDS artifact.</summary>
public interface IGdsScriptRunner
{
    Task<GdsExportResult> ExportToGdsAsync(string scriptPath, bool generateGds);
}
