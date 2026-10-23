namespace CAP_Core.Export.Contracts;

/// <summary>A script runner and its selected, probed Python environment.</summary>
public interface IGdsExportSession : IGdsScriptRunner
{
    void SetCustomPythonPath(string? pythonPath);
    string GetCurrentPythonPath();
    Task<PythonEnvironmentInfo> CheckPythonEnvironmentAsync(CancellationToken cancellationToken = default);
}
