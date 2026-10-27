using CAP_Core.Export.Contracts;
using CAP_Core.Export.Processes;

namespace CAP_Core.Export;

/// <summary>
/// Runs the Python gdspy coordinate extraction script on a GDS file
/// and returns structured JSON output for numerical comparison.
/// Supports debugging coordinate mismatches between component pins
/// and waveguide placements (issue #329).
/// </summary>
public class GdsCoordinateExtractor : IGdsCoordinateExtractor
{
    private const string ScriptRelativePath = "scripts/extract_gds_coords.py";
    private readonly CoordinateExecutionOptions _execution;

    private readonly ProcessLaunchFactory _launchFactory;
    private string? _customPythonPath;

    /// <summary>
    /// Initializes a new instance of <see cref="GdsCoordinateExtractor"/>.
    /// </summary>
    /// <param name="launchFactory">Factory used to build process start info.</param>
    public GdsCoordinateExtractor(ProcessLaunchFactory? launchFactory = null)
        : this(launchFactory, new CoordinateExecutionOptions(TimeSpan.FromSeconds(30)))
    {
    }

    internal GdsCoordinateExtractor(ProcessLaunchFactory? launchFactory, CoordinateExecutionOptions execution)
    {
        _launchFactory = launchFactory ?? ProcessLaunchFactory.CreateDefault();
        _execution = execution;
    }


    /// <summary>Sets a custom Python executable path to use instead of the system default.</summary>
    /// <param name="pythonPath">Path to Python executable, or null to use system default.</param>
    public void SetCustomPythonPath(string? pythonPath)
    {
        _customPythonPath = pythonPath;
    }

    /// <summary>
    /// Extracts all polygon and path coordinates from a GDS file using the
    /// scripts/extract_gds_coords.py Python script.
    /// </summary>
    /// <param name="gdsPath">Path to the GDS file to analyze.</param>
    /// <param name="outputJsonPath">Output JSON path; defaults to .coords.json alongside the GDS file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extraction result containing the JSON coordinate data.</returns>
    public async Task<GdsCoordinateExtractionResult> ExtractAsync(
        string gdsPath,
        string? outputJsonPath = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(gdsPath))
            return Failure($"GDS file not found: {gdsPath}");

        var scriptPath = _execution.ScriptPath ?? FindScriptPath();
        if (scriptPath == null)
            return Failure($"Extraction script not found. Expected at: {ScriptRelativePath}");

        var jsonPath = outputJsonPath ?? Path.ChangeExtension(gdsPath, ".coords.json");

        try
        {
            var command = string.IsNullOrEmpty(_customPythonPath)
                ? _launchFactory.ResolveExecutable("python3") ?? "python3"
                : _customPythonPath;

            var args = new[] { scriptPath, gdsPath, jsonPath };
            var workingDir = Path.GetDirectoryName(scriptPath);

            if (!_launchFactory.TryBuild(command, args, workingDir, null, out var psi, out var launchError))
                return Failure($"Failed to build process: {launchError}");

            var captured = await OwnedProcessCapture.RunAsync(psi, _execution.Timeout, cancellationToken);
            if (captured.TimedOut)
                return Failure($"Timeout after {(int)_execution.Timeout.TotalSeconds}s");

            if (captured.ExitCode != 0)
            {
                var msg = string.IsNullOrWhiteSpace(captured.Error) ? captured.Output : captured.Error;
                return Failure($"Script failed (exit {captured.ExitCode}): {msg}");
            }

            if (!File.Exists(jsonPath))
                return Failure("Script ran but no output JSON was created.");

            var content = await File.ReadAllTextAsync(jsonPath, cancellationToken);
            return new GdsCoordinateExtractionResult
            {
                Success = true,
                JsonOutputPath = jsonPath,
                JsonContent = content,
                Status = $"Extracted to {Path.GetFileName(jsonPath)}"
            };
        }
        catch (OperationCanceledException)
        {
            return Failure("Extraction was cancelled.");
        }
        catch (Exception ex)
        {
            return Failure($"Error running extraction script: {ex.Message}");
        }
    }

    /// <summary>Checks whether Python is available for running the extraction script.</summary>
    /// <returns>True if Python is available.</returns>
    public async Task<bool> IsPythonAvailableAsync()
    {
        try
        {
            var command = string.IsNullOrEmpty(_customPythonPath)
                ? _launchFactory.ResolveExecutable("python3") ?? "python3"
                : _customPythonPath;

            var arguments = _execution.ProbeArguments.IsDefault
                ? new[] { "--version" } : _execution.ProbeArguments.ToArray();
            if (!_launchFactory.TryBuild(command, arguments, null, null, out var psi, out _))
                return false;

            var captured = await OwnedProcessCapture.RunAsync(psi, _execution.Timeout);
            return !captured.TimedOut && captured.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves the path to the extraction script.
    /// Searches relative to the executable, current directory, and walks up
    /// the directory tree to locate the repository root (useful in test contexts).
    /// </summary>
    public static string? FindScriptPath()
    {
        var candidateDirs = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var dir in candidateDirs)
        {
            // Direct match
            var direct = Path.Combine(dir, ScriptRelativePath);
            if (File.Exists(direct))
                return direct;

            // Walk up to find a directory containing the Scripts folder
            var current = dir;
            for (var i = 0; i < 8; i++)
            {
                var candidate = Path.Combine(current, ScriptRelativePath);
                if (File.Exists(candidate))
                    return candidate;
                var parent = Path.GetDirectoryName(current);
                if (parent == null || parent == current)
                    break;
                current = parent;
            }
        }

        return null;
    }

    private static GdsCoordinateExtractionResult Failure(string message) =>
        new() { Success = false, ErrorMessage = message, Status = message };
}
