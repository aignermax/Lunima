using System.Diagnostics;
using CAP_Core.Export.Processes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CAP_Core.Export;

/// <summary>
/// Invokes the <c>scripts/parse_pdk.py</c> Python script to extract component
/// geometry from a Nazca PDK module and returns the result as a structured object.
///
/// Issue #460: PDK Import Tool — Python/Nazca PDK parser with visual verification.
/// </summary>
public class PdkNazcaParserService
{
    /// <summary>
    /// Hard wall-clock cap on the Python subprocess. A hung interpreter (blocking
    /// <c>input()</c> in a user's module, slow network import, infinite loop) would
    /// otherwise lock the wizard until the app is killed. 60s is generous for
    /// parsing even large PDKs.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private readonly ProcessLaunchFactory _launchFactory;
    private readonly string _pythonExecutable;
    private readonly string _scriptPath;
    private readonly TimeSpan _timeout;

    /// <summary>
    /// Creates a <see cref="PdkNazcaParserService"/> with explicit paths.
    /// </summary>
    /// <param name="launchFactory">Factory used to build <see cref="ProcessStartInfo"/> instances.</param>
    /// <param name="pythonExecutable">Path to Python 3 executable (e.g. "python3").</param>
    /// <param name="scriptPath">Absolute path to <c>scripts/parse_pdk.py</c>.</param>
    /// <param name="timeout">Optional subprocess timeout. Defaults to <see cref="DefaultTimeout"/>.</param>
    public PdkNazcaParserService(string pythonExecutable, string scriptPath, TimeSpan? timeout = null, ProcessLaunchFactory? launchFactory = null)
    {
        _launchFactory = launchFactory ?? ProcessLaunchFactory.CreateDefault();
        _pythonExecutable = pythonExecutable ?? throw new ArgumentNullException(nameof(pythonExecutable));
        _scriptPath = scriptPath ?? throw new ArgumentNullException(nameof(scriptPath));
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>
    /// Runs the parser script for a given PDK module and returns parsed component data.
    /// </summary>
    /// <param name="moduleName">
    /// Python module name to import (e.g. <c>"siepic_ebeam_pdk"</c>).
    /// Pass <c>"demo"</c> to get hard-coded reference values without Nazca.
    /// </param>
    /// <param name="functionNames">
    /// Optional list of specific cell function names to parse.
    /// When null or empty, all public callables in the module are parsed.
    /// </param>
    /// <param name="additionalPythonPath">
    /// Optional directory to prepend to PYTHONPATH, allowing the module to be located
    /// by its file path (e.g. when the user selects a .py file directly).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed PDK data including component geometry and origin offsets.</returns>
    /// <exception cref="FileNotFoundException">When the parse script is not found.</exception>
    /// <exception cref="PdkParserException">When the script exits with a non-zero code.</exception>
    public async Task<PdkParseResult> ParseAsync(
        string moduleName,
        IEnumerable<string>? functionNames = null,
        string? additionalPythonPath = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_scriptPath))
            throw new FileNotFoundException($"PDK parser script not found: {_scriptPath}", _scriptPath);

        var (exitCode, stdout, stderr) = await RunScriptAsync(moduleName, functionNames, additionalPythonPath, cancellationToken);

        if (exitCode != 0)
            throw new PdkParserException(
                $"parse_pdk.py exited with code {exitCode}. " +
                $"stderr: {stderr.Trim()}");

        if (string.IsNullOrWhiteSpace(stdout))
            throw new PdkParserException("parse_pdk.py produced no output.");

        return DeserializeResult(stdout);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task<(int exitCode, string stdout, string stderr)> RunScriptAsync(
        string moduleName, IEnumerable<string>? functionNames,
        string? additionalPythonPath, CancellationToken cancellationToken)
    {
        var args = BuildArgumentList(_scriptPath, moduleName, functionNames);
        var extraEnv = BuildExtraEnv(additionalPythonPath);

        if (!_launchFactory.TryBuild(
                _pythonExecutable, args,
                Path.GetDirectoryName(_scriptPath),
                extraEnv,
                out var psi, out var launchError))
        {
            throw new PdkParserException(
                $"Could not start Python executable '{_pythonExecutable}'. " +
                $"Check the Python path in Preferences or ensure python3 is on PATH. ({launchError})");
        }

        CapturedProcessResult result;
        try
        {
            result = await OwnedProcessCapture.RunAsync(psi, _timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new PdkParserException(
                $"Could not start Python executable '{_pythonExecutable}'. " +
                "Check the Python path in Preferences or ensure python3 is on PATH.", ex);
        }

        if (result.TimedOut)
            throw new PdkParserException(
                $"parse_pdk.py did not finish within {_timeout.TotalSeconds:F0}s. " +
                "The module may have blocked on input or entered an infinite loop.");
        return (result.ExitCode, result.Output, result.Error);
    }

    private static IReadOnlyList<string> BuildArgumentList(string scriptPath, string moduleName, IEnumerable<string>? functionNames)
    {
        var args = new List<string> { scriptPath, moduleName };
        if (functionNames != null)
            args.AddRange(functionNames);
        return args;
    }

    private static Dictionary<string, string>? BuildExtraEnv(string? additionalPythonPath)
    {
        if (string.IsNullOrEmpty(additionalPythonPath))
            return null;

        var existing = Environment.GetEnvironmentVariable("PYTHONPATH") ?? "";
        var separator = Path.PathSeparator.ToString();
        var pythonPath = string.IsNullOrEmpty(existing)
            ? additionalPythonPath
            : $"{additionalPythonPath}{separator}{existing}";

        return new Dictionary<string, string> { ["PYTHONPATH"] = pythonPath };
    }

    private static PdkParseResult DeserializeResult(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        var result = JsonSerializer.Deserialize<PdkParseResult>(json, options)
            ?? throw new PdkParserException("Failed to deserialize parse_pdk.py output.");

        return result;
    }
}

/// <summary>
/// Thrown when <see cref="PdkNazcaParserService"/> fails to parse PDK data.
/// </summary>
public class PdkParserException : Exception
{
    /// <summary>Initializes a new <see cref="PdkParserException"/> with a message.</summary>
    public PdkParserException(string message) : base(message) { }

    /// <summary>Initializes a new <see cref="PdkParserException"/> with message and inner exception.</summary>
    public PdkParserException(string message, Exception inner) : base(message, inner) { }
}
