using CAP_Core.Export.Processes;
using CAP_Core.Export.Contracts;
using System.Diagnostics;

namespace CAP_Core.Export;

/// <summary>
/// Core service for GDS export functionality.
/// Detects Python/Nazca availability and executes Python scripts to generate GDS files.
/// </summary>
public class GdsExportService : IGdsExportSession
{
    private const string MinimumNazcaVersion = "0.5.0";
    private const string DefaultPythonCommand = "python3";

    private readonly ProcessLaunchFactory _launchFactory;
    private readonly IPythonDiscovery _pythonDiscovery;
    private string? _customPythonPath;

    /// <summary>
    /// The last READY environment and the interpreter setting it was probed with. An export
    /// reuses it instead of re-probing (each probe spawns python and imports Nazca —
    /// seconds); a not-ready result is never kept, so a fresh Nazca install is seen at once.
    /// </summary>
    private (string? Interpreter, PythonEnvironmentInfo Info)? _readyEnvironment;

    /// <summary>
    /// Initializes the service with a process launch factory and Python discovery service.
    /// </summary>
    /// <param name="launchFactory">Factory used to build process start info.</param>
    /// <param name="pythonDiscovery">Service used to locate a Python interpreter with Nazca.</param>
    public GdsExportService(ProcessLaunchFactory? launchFactory = null, IPythonDiscovery? pythonDiscovery = null)
    {
        _launchFactory   = launchFactory   ?? ProcessLaunchFactory.CreateDefault();
        _pythonDiscovery = pythonDiscovery ?? new PythonDiscoveryService(_launchFactory);
    }

    /// <summary>
    /// Sets a custom Python path to use instead of system default.
    /// </summary>
    /// <param name="pythonPath">Path to Python executable, or null to use system default.</param>
    public void SetCustomPythonPath(string? pythonPath)
    {
        if (pythonPath != _customPythonPath)
            _readyEnvironment = null;
        _customPythonPath = pythonPath;
    }

    /// <summary>
    /// Gets the currently configured Python path (custom or default).
    /// </summary>
    public string GetCurrentPythonPath()
    {
        return _customPythonPath ?? _launchFactory.ResolveExecutable(DefaultPythonCommand) ?? DefaultPythonCommand;
    }

    /// <summary>
    /// Checks if Python and Nazca are available in the system.
    /// Uses custom Python path if configured, otherwise uses system default.
    /// </summary>
    /// <returns>Environment information including versions.</returns>
    public async Task<PythonEnvironmentInfo> CheckPythonEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        var result = await ProbeEnvironmentAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _readyEnvironment = result.IsReady ? (_customPythonPath, result) : null;
        return result;
    }

    /// <summary>
    /// Probes the configured interpreter: its version, then whether it imports Nazca.
    /// </summary>
    /// <returns>Environment information including versions.</returns>
    protected virtual async Task<PythonEnvironmentInfo> ProbeEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        var result = new PythonEnvironmentInfo();

        var pythonVersion = await GetPythonVersionAsync(cancellationToken);
        if (!string.IsNullOrEmpty(pythonVersion))
        {
            result.PythonAvailable = true;
            result.PythonVersion = pythonVersion;

            var nazcaVersion = await GetNazcaVersionAsync(cancellationToken);
            if (!string.IsNullOrEmpty(nazcaVersion))
            {
                result.NazcaAvailable = true;
                result.NazcaVersion = nazcaVersion;
            }
        }

        return result;
    }

    /// <summary>
    /// Exports to GDS by executing a Python script.
    /// </summary>
    /// <param name="scriptPath">Path to the Python script to execute.</param>
    /// <param name="generateGds">If true, attempts to generate GDS from the script.</param>
    /// <returns>Export result with status information.</returns>
    public virtual async Task<GdsExportResult> ExportToGdsAsync(string scriptPath, bool generateGds)
    {
        if (!File.Exists(scriptPath))
        {
            return new GdsExportResult
            {
                ScriptPath = scriptPath,
                Success = false,
                ErrorMessage = "Script file not found"
            };
        }

        if (!generateGds)
        {
            return new GdsExportResult
            {
                ScriptPath = scriptPath,
                Success = true,
                Status = "Script exported (GDS generation skipped)"
            };
        }

        var envInfo = _readyEnvironment is { } ready && ready.Interpreter == _customPythonPath
            ? ready.Info
            : await CheckPythonEnvironmentAsync();
        if (!envInfo.IsReady)
        {
            return new GdsExportResult
            {
                ScriptPath = scriptPath,
                Success = false,
                Status = $"GDS generation skipped: {envInfo.StatusMessage}",
                ErrorMessage = envInfo.StatusMessage
            };
        }

        try
        {
            var gdsPath = Path.ChangeExtension(scriptPath, ".gds");
            var (exitCode, output, error) = await ExecutePythonScriptAsync(scriptPath);

            if (exitCode == 0 && File.Exists(gdsPath))
            {
                return new GdsExportResult
                {
                    ScriptPath = scriptPath,
                    GdsPath = gdsPath,
                    Success = true,
                    Status = $"Script and GDS exported successfully"
                };
            }

            var errorMsg = string.IsNullOrWhiteSpace(error) ? output : error;
            return new GdsExportResult
            {
                ScriptPath = scriptPath,
                Success = false,
                Status = "GDS generation failed",
                ErrorMessage = $"Python script execution failed (exit code {exitCode}): {errorMsg}"
            };
        }
        catch (Exception ex)
        {
            return new GdsExportResult
            {
                ScriptPath = scriptPath,
                Success = false,
                Status = "GDS generation failed",
                ErrorMessage = $"Error executing Python: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Resolves the Python command to use: custom path, then discovery service, then fallback.
    /// </summary>
    private async Task<string> ResolvePythonCommandAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_customPythonPath))
            return _customPythonPath;

        var discovered = await _pythonDiscovery.FindFirstNazcaPythonPathAsync(cancellationToken);
        if (!string.IsNullOrEmpty(discovered))
            return discovered;

        return _launchFactory.ResolveExecutable(DefaultPythonCommand) ?? DefaultPythonCommand;
    }

    /// <summary>
    /// Gets the Python version string.
    /// </summary>
    private async Task<string?> GetPythonVersionAsync(CancellationToken cancellationToken)
    {
        var output = await ProbeVersionAsync(new[] { "--version" }, TimeSpan.FromSeconds(5), cancellationToken);
        if (string.IsNullOrWhiteSpace(output)) return null;
        var parts = output.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : output.Trim();
    }

    private Task<string?> GetNazcaVersionAsync(CancellationToken cancellationToken) =>
        ProbeVersionAsync(new[] { "-c", "import nazca; print(nazca.__version__)" }, TimeSpan.FromSeconds(20), cancellationToken);

    private async Task<string?> ProbeVersionAsync(string[] arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = await ResolvePythonCommandAsync(cancellationToken);
            if (!_launchFactory.TryBuild(command, arguments, null, null, out var startInfo, out _)) return null;
            var result = await PythonProbeRunner.RunAsync(startInfo, timeout, cancellationToken);
            return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output) ? result.Output.Trim() : null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return null; }
    }
    /// <summary>
    /// Executes a Python script file.
    /// </summary>
    private async Task<(int exitCode, string output, string error)> ExecutePythonScriptAsync(string scriptPath)
    {
        var pythonCmd    = await ResolvePythonCommandAsync();
        var args         = new[] { scriptPath };
        var workingDir   = Path.GetDirectoryName(scriptPath);

        // PYTHONSAFEPATH: a leftover re.py/numpy.py NEXT TO the exported script must not
        // shadow the modules the Nazca run imports (see PythonModuleShadowing).
        if (!_launchFactory.TryBuild(pythonCmd, args, workingDir,
                PythonModuleShadowing.SafePathEnvironment, out var psi, out var launchError))
            return (-1, string.Empty, launchError ?? "Failed to build process start info");

        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError  = true;
        using var process = Process.Start(psi);
        if (process == null)
            return (-1, string.Empty, "Failed to start Python process");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask  = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error  = await errorTask;

        return (process.ExitCode, output, error);
    }
}
