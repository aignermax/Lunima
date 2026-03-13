using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CAP_Core.Export;

/// <summary>
/// Core service for GDS export functionality.
/// Detects Python/Nazca availability and executes Python scripts to generate GDS files.
/// </summary>
public class GdsExportService
{
    private const string MinimumNazcaVersion = "0.5.0";

    /// <summary>
    /// Result of a GDS export operation.
    /// </summary>
    public class ExportResult
    {
        /// <summary>
        /// Path to the exported Python script.
        /// </summary>
        public string ScriptPath { get; init; } = string.Empty;

        /// <summary>
        /// Path to the generated GDS file (null if generation failed or was skipped).
        /// </summary>
        public string? GdsPath { get; init; }

        /// <summary>
        /// Status message describing the export outcome.
        /// </summary>
        public string Status { get; init; } = string.Empty;

        /// <summary>
        /// True if both script and GDS were successfully created.
        /// </summary>
        public bool Success { get; init; }

        /// <summary>
        /// Error message if export failed.
        /// </summary>
        public string? ErrorMessage { get; init; }
    }

    /// <summary>
    /// Information about Python environment availability.
    /// </summary>
    public class PythonEnvironmentInfo
    {
        /// <summary>
        /// True if Python executable was found.
        /// </summary>
        public bool PythonAvailable { get; set; }

        /// <summary>
        /// Python version string (e.g., "3.10.5").
        /// </summary>
        public string? PythonVersion { get; set; }

        /// <summary>
        /// True if Nazca package is installed.
        /// </summary>
        public bool NazcaAvailable { get; set; }

        /// <summary>
        /// Nazca version string (e.g., "0.5.10").
        /// </summary>
        public string? NazcaVersion { get; set; }

        /// <summary>
        /// True if both Python and Nazca are available.
        /// </summary>
        public bool IsReady => PythonAvailable && NazcaAvailable;

        /// <summary>
        /// Descriptive status message for UI display.
        /// </summary>
        public string StatusMessage
        {
            get
            {
                if (!PythonAvailable)
                    return "Python not found";
                if (!NazcaAvailable)
                    return "Nazca not installed";
                return $"Python {PythonVersion}, Nazca {NazcaVersion}";
            }
        }
    }

    /// <summary>
    /// Checks if Python and Nazca are available in the system.
    /// </summary>
    /// <returns>Environment information including versions.</returns>
    public async Task<PythonEnvironmentInfo> CheckPythonEnvironmentAsync()
    {
        var result = new PythonEnvironmentInfo();

        // Check Python
        var pythonVersion = await GetPythonVersionAsync();
        if (!string.IsNullOrEmpty(pythonVersion))
        {
            result.PythonAvailable = true;
            result.PythonVersion = pythonVersion;

            // If Python is available, check for Nazca
            var nazcaVersion = await GetNazcaVersionAsync();
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
    public async Task<ExportResult> ExportToGdsAsync(string scriptPath, bool generateGds)
    {
        if (!File.Exists(scriptPath))
        {
            return new ExportResult
            {
                ScriptPath = scriptPath,
                Success = false,
                ErrorMessage = "Script file not found"
            };
        }

        if (!generateGds)
        {
            return new ExportResult
            {
                ScriptPath = scriptPath,
                Success = true,
                Status = "Script exported (GDS generation skipped)"
            };
        }

        // Check environment
        var envInfo = await CheckPythonEnvironmentAsync();
        if (!envInfo.IsReady)
        {
            return new ExportResult
            {
                ScriptPath = scriptPath,
                Success = false,
                Status = $"GDS generation skipped: {envInfo.StatusMessage}",
                ErrorMessage = envInfo.StatusMessage
            };
        }

        // Execute Python script
        try
        {
            var gdsPath = Path.ChangeExtension(scriptPath, ".gds");
            var (exitCode, output, error) = await ExecutePythonScriptAsync(scriptPath);

            if (exitCode == 0 && File.Exists(gdsPath))
            {
                return new ExportResult
                {
                    ScriptPath = scriptPath,
                    GdsPath = gdsPath,
                    Success = true,
                    Status = $"Script and GDS exported successfully"
                };
            }

            var errorMsg = string.IsNullOrWhiteSpace(error) ? output : error;
            return new ExportResult
            {
                ScriptPath = scriptPath,
                Success = false,
                Status = "GDS generation failed",
                ErrorMessage = $"Python script execution failed (exit code {exitCode}): {errorMsg}"
            };
        }
        catch (Exception ex)
        {
            return new ExportResult
            {
                ScriptPath = scriptPath,
                Success = false,
                Status = "GDS generation failed",
                ErrorMessage = $"Error executing Python: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Gets the Python version string.
    /// </summary>
    private async Task<string?> GetPythonVersionAsync()
    {
        try
        {
            var pythonCmd = GetPythonCommand();
            var (exitCode, output, _) = await RunCommandAsync(pythonCmd, "--version");

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                // Python version output: "Python 3.10.5"
                var parts = output.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 2 ? parts[1] : output.Trim();
            }
        }
        catch
        {
            // Python not found
        }

        return null;
    }

    /// <summary>
    /// Gets the Nazca version string.
    /// </summary>
    private async Task<string?> GetNazcaVersionAsync()
    {
        try
        {
            var pythonCmd = GetPythonCommand();
            var checkScript = "import nazca; print(nazca.__version__)";
            var (exitCode, output, _) = await RunCommandAsync(pythonCmd, $"-c \"{checkScript}\"");

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                return output.Trim();
            }
        }
        catch
        {
            // Nazca not installed
        }

        return null;
    }

    /// <summary>
    /// Executes a Python script file.
    /// </summary>
    private async Task<(int exitCode, string output, string error)> ExecutePythonScriptAsync(string scriptPath)
    {
        var pythonCmd = GetPythonCommand();
        return await RunCommandAsync(pythonCmd, $"\"{scriptPath}\"");
    }

    /// <summary>
    /// Runs a command and captures output.
    /// </summary>
    private async Task<(int exitCode, string output, string error)> RunCommandAsync(
        string fileName, string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var output = await outputTask;
        var error = await errorTask;

        return (process.ExitCode, output, error);
    }

    /// <summary>
    /// Gets the Python command name for the current platform.
    /// </summary>
    private static string GetPythonCommand()
    {
        // On Windows, try "python" first (most common)
        // On Unix-like systems, try "python3" first
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "python" : "python3";
    }
}
