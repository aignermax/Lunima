using CAP_Core.Export.Contracts;
using System.Diagnostics;
using System.ComponentModel;
using CAP_Core.Export.Processes;
using System.Text.Json;

namespace CAP_Core.Export;

/// <summary>
/// Core service for comparing two GDS coordinate JSON files using the
/// scripts/compare_gds_coords.py tool.  Reports exact deviations in micrometres
/// so fabrication-blocking alignment bugs can be confirmed or refuted.
/// </summary>
public class GdsCoordinateComparisonService : IGdsCoordinateComparer
{
    private readonly ProcessLaunchFactory _launchFactory;
    private readonly CoordinateExecutionOptions _execution;


    private string? _customPythonPath;

    /// <summary>
    /// Initializes the service with the required process launch factory.
    /// </summary>
    /// <param name="launchFactory">Factory used to build <see cref="ProcessStartInfo"/> instances.</param>
    public GdsCoordinateComparisonService(ProcessLaunchFactory? launchFactory = null)
        : this(launchFactory, new CoordinateExecutionOptions(TimeSpan.FromSeconds(30)))
    {
    }

    internal GdsCoordinateComparisonService(ProcessLaunchFactory? launchFactory, CoordinateExecutionOptions execution)
    {
        _launchFactory = launchFactory ?? ProcessLaunchFactory.CreateDefault();
        _execution = execution;
    }

    /// <summary>
    /// Sets a custom Python executable path.  When null the system default is used.
    /// </summary>
    /// <param name="pythonPath">Absolute path to Python executable, or null.</param>
    public void SetCustomPythonPath(string? pythonPath) => _customPythonPath = pythonPath;

    /// <summary>
    /// Runs <c>compare_gds_coords.py</c> on two JSON coordinate files.
    /// </summary>
    /// <param name="referenceJsonPath">Path to the reference coordinate JSON.</param>
    /// <param name="systemJsonPath">Path to the system coordinate JSON to compare.</param>
    /// <param name="scriptPath">
    /// Absolute path to compare_gds_coords.py.
    /// When null, <see cref="FindDefaultScriptPath"/> is used.
    /// </param>
    /// <returns>Structured comparison result.</returns>
    public Task<GdsCoordinateComparisonResult> CompareAsync(
        string referenceJsonPath, string systemJsonPath, string? scriptPath = null)
        => CompareAsync(referenceJsonPath, systemJsonPath, scriptPath, CancellationToken.None);

    public async Task<GdsCoordinateComparisonResult> CompareAsync(
        string referenceJsonPath, string systemJsonPath, string? scriptPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolvedScript = scriptPath ?? FindDefaultScriptPath();
        if (resolvedScript == null || !File.Exists(resolvedScript))
            return ErrorResult($"Script not found: {resolvedScript ?? "(null)"}");

        if (!File.Exists(referenceJsonPath))
            return ErrorResult($"Reference JSON not found: {referenceJsonPath}");

        if (!File.Exists(systemJsonPath))
            return ErrorResult($"System JSON not found: {systemJsonPath}");

        var reportPath = Path.Combine(Path.GetTempPath(), $"cap_comparison_{Guid.NewGuid():N}.json");
        var scriptArgs = new[] { resolvedScript, referenceJsonPath, systemJsonPath, reportPath };

        var command = string.IsNullOrEmpty(_customPythonPath) ? "python3" : _customPythonPath;
        if (!_launchFactory.TryBuild(command, scriptArgs, Path.GetDirectoryName(resolvedScript), null, out var psi, out var launchError))
            return ErrorResult($"Failed to build process: {launchError}");

        try
        {
            var captured = await OwnedProcessCapture.RunAsync(psi, _execution.Timeout, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (captured.TimedOut)
                return new GdsCoordinateComparisonResult
                {
                    Passed = false,
                    ErrorOutput = $"Timeout after {(int)_execution.Timeout.TotalSeconds}s"
                };
            return ParseResult(captured.ExitCode, captured.Output, captured.Error, reportPath);
        }
        catch (Win32Exception error)
        {
            // Native launch errors are localized and may omit the executable name.
            // Keep the original exception contract/code while retaining actionable context.
            throw new Win32Exception(error.NativeErrorCode, $"Could not run '{psi.FileName}': {error.Message}");
        }
        finally
        {
            // The process scope is already retired; no surviving writer can recreate the report.
            if (File.Exists(reportPath)) File.Delete(reportPath);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private GdsCoordinateComparisonResult ParseResult(int exitCode, string stdout, string stderr, string reportPath)
    {
        try
        {
            if (File.Exists(reportPath))
            {
                var json = File.ReadAllText(reportPath);
                File.Delete(reportPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                return new GdsCoordinateComparisonResult
                {
                    Passed = root.GetProperty("passed").GetBoolean(),
                    MaxDeviationUm = root.GetProperty("max_deviation_um").GetDouble(),
                    RmsDeviationUm = root.GetProperty("rms_deviation_um").GetDouble(),
                    ToleranceUm = root.GetProperty("tolerance_um").GetDouble(),
                    RawOutput = stdout,
                    ErrorOutput = stderr
                };
            }
        }
        catch (Exception ex)
        {
            return ErrorResult($"Failed to parse report: {ex.Message}\nStdout: {stdout}\nStderr: {stderr}");
        }

        return new GdsCoordinateComparisonResult
        {
            Passed = exitCode == 0,
            RawOutput = stdout,
            ErrorOutput = stderr
        };
    }

    private static GdsCoordinateComparisonResult ErrorResult(string message) => new()
    {
        Passed = false,
        RawOutput = message,
        ErrorOutput = message
    };

    /// <summary>
    /// Searches for <c>scripts/compare_gds_coords.py</c> by walking up from the
    /// current assembly directory.  Returns null if not found.
    /// </summary>
    public static string? FindDefaultScriptPath()
    {
        const string RelativePath = "scripts/compare_gds_coords.py";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, RelativePath);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
