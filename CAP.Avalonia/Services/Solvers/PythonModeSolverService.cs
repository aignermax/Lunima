using System.Text.Json;
using CAP_Core.Export;
using CAP_Core.Solvers.ModeSolver;

namespace CAP.Avalonia.Services.Solvers;

/// <summary>
/// Implements <see cref="IModeSolverService"/> by invoking <c>scripts/mode_solve.py</c>
/// as a Python subprocess.  The JSON request is written to stdin; the JSON result
/// is read from stdout.  Raw stderr is preserved and surfaced on failure — no silent
/// fallback to a guessed n_eff.
/// </summary>
public class PythonModeSolverService : IModeSolverService
{
    /// <summary>Default subprocess timeout (120 s — FDE can be slow on coarse grids).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    private readonly Func<string> _pythonExecutableResolver;
    private readonly string _scriptPath;
    private readonly TimeSpan _timeout;
    private readonly ProcessLaunchFactory _launchFactory;

    /// <summary>
    /// Initializes the service with a lazily-resolved interpreter. The resolver is
    /// invoked on every <see cref="SolveAsync"/> call, so a newly activated or
    /// freshly auto-installed managed environment is picked up without an app
    /// restart (#691 review).
    /// </summary>
    /// <param name="pythonExecutableResolver">Returns the Python 3 executable to use for the next solve.</param>
    /// <param name="scriptPath">Absolute path to <c>mode_solve.py</c>.</param>
    /// <param name="timeout">Optional subprocess timeout.</param>
    /// <param name="launchFactory">Optional platform-aware process configuration factory.</param>
    public PythonModeSolverService(Func<string> pythonExecutableResolver, string scriptPath,
        TimeSpan? timeout = null, ProcessLaunchFactory? launchFactory = null)
    {
        _pythonExecutableResolver = pythonExecutableResolver ?? throw new ArgumentNullException(nameof(pythonExecutableResolver));
        _scriptPath = scriptPath ?? throw new ArgumentNullException(nameof(scriptPath));
        _timeout = timeout ?? DefaultTimeout;
        _launchFactory = launchFactory ?? ProcessLaunchFactory.CreateDefault();
    }

    /// <summary>Initializes the service with a fixed interpreter path.</summary>
    /// <param name="pythonExecutable">Path to the Python 3 executable.</param>
    /// <param name="scriptPath">Absolute path to <c>mode_solve.py</c>.</param>
    /// <param name="timeout">Optional subprocess timeout.</param>
    /// <param name="launchFactory">Optional platform-aware process configuration factory.</param>
    public PythonModeSolverService(string pythonExecutable, string scriptPath,
        TimeSpan? timeout = null, ProcessLaunchFactory? launchFactory = null)
        : this(Constant(pythonExecutable), scriptPath, timeout, launchFactory)
    {
    }

    private static Func<string> Constant(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return () => value;
    }

    /// <inheritdoc/>
    public async Task<ModeSolverResult> SolveAsync(ModeSolverRequest request, CancellationToken ct = default)
    {
        if (!File.Exists(_scriptPath))
            return ModeSolverResult.Fail($"Mode-solver script not found: {_scriptPath}");

        var jsonInput = SerialiseRequest(request);
        var pythonExecutable = _pythonExecutableResolver();

        try
        {
            if (!_launchFactory.TryBuild(pythonExecutable, new[] { _scriptPath },
                    null, null, out var startInfo, out var error))
                return ModeSolverResult.Fail($"Could not start Python '{pythonExecutable}': {error}");

            var run = await SubprocessJsonRunner.RunAsync(startInfo, jsonInput, _timeout, ct);
            return run.Outcome switch
            {
                SubprocessJsonRunner.Outcome.StartFailed => ModeSolverResult.Fail(
                    $"Could not start Python '{pythonExecutable}': {run.StartError}"),
                SubprocessJsonRunner.Outcome.Cancelled => ModeSolverResult.Fail("Mode solve was cancelled."),
                SubprocessJsonRunner.Outcome.TimedOut => ModeSolverResult.Fail(
                    $"Mode solver timed out after {_timeout.TotalSeconds:F0} s."),
                _ => ParseOutput(run.Stdout, run.Stderr),
            };
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return ModeSolverResult.Fail($"Could not start Python '{pythonExecutable}': {ex.Message}");
        }
        catch (Exception ex)
        {
            return ModeSolverResult.Fail($"Unexpected error launching mode solver: {ex.Message}");
        }
    }

    /// <summary>
    /// Serialises a <see cref="ModeSolverRequest"/> to the JSON format expected by
    /// <c>mode_solve.py</c>.
    /// </summary>
    private static string SerialiseRequest(ModeSolverRequest req)
    {
        var obj = new
        {
            width       = req.Width,
            height      = req.Height,
            slab_height = req.SlabHeight,
            core_index  = req.CoreIndex,
            clad_index  = req.CladIndex,
            wavelengths = req.Wavelengths,
            backend     = req.Backend.ToString(),
            num_modes   = req.NumModes,
        };
        return JsonSerializer.Serialize(obj);
    }

    /// <summary>
    /// Parses the JSON written by <c>mode_solve.py</c> on stdout.
    /// Exposed as <c>internal</c> so unit tests can exercise the JSON path
    /// without spawning a real subprocess.
    /// </summary>
    internal static ModeSolverResult ParseOutput(string stdout, string stderr = "")
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return ModeSolverResult.Fail("Mode solver produced no output.", rawStderr: stderr);

        var jsonLine = ExtractTrailingJsonLine(stdout);
        if (jsonLine == null)
            return ModeSolverResult.Fail(
                $"No JSON found in mode-solver output: {Truncate(stdout, 300)}",
                rawStderr: stderr);

        try
        {
            using var doc  = JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var sp) && !sp.GetBoolean())
            {
                var error          = root.TryGetProperty("error",           out var ep) ? ep.GetString() : null;
                var missingBackend = root.TryGetProperty("missing_backend", out var mb) ? mb.GetString() : null;
                return ModeSolverResult.Fail(
                    error ?? "Unknown mode-solver error",
                    rawStderr: stderr,
                    missingBackend: missingBackend);
            }

            var backendUsed = root.TryGetProperty("backend_used", out var bu) ? bu.GetString() : null;
            var modes       = ParseModes(root);

            return new ModeSolverResult
            {
                Success     = true,
                BackendUsed = backendUsed,
                Modes       = modes,
            };
        }
        catch (Exception ex)
        {
            return ModeSolverResult.Fail(
                $"Failed to parse mode-solver output: {ex.Message}",
                rawStderr: stderr);
        }
    }

    private static List<ModeSolverModeEntry> ParseModes(JsonElement root)
    {
        var list = new List<ModeSolverModeEntry>();
        if (!root.TryGetProperty("modes", out var arr))
            return list;

        foreach (var m in arr.EnumerateArray())
        {
            list.Add(new ModeSolverModeEntry
            {
                Wavelength   = m.TryGetProperty("wavelength",    out var wl)  ? wl.GetDouble()  : 0,
                ModeIndex    = m.TryGetProperty("mode_index",    out var mi)  ? mi.GetInt32()   : 0,
                NEff         = m.TryGetProperty("n_eff",         out var ne)  ? ne.GetDouble()  : 0,
                NGroup       = m.TryGetProperty("n_g",           out var ng)  ? ng.GetDouble()  : 0,
                Polarisation = m.TryGetProperty("polarisation",  out var pol) ? pol.GetString() ?? "" : "",
                ModeFieldPng = m.TryGetProperty("mode_field_png",out var png) ? png.GetString() : null,
            });
        }
        return list;
    }

    /// <summary>
    /// Walks stdout from the bottom up and returns the first line that parses as
    /// a JSON object — the same technique used by <c>NazcaComponentPreviewService</c>
    /// to handle Nazca log chatter on stdout.
    /// </summary>
    private static string? ExtractTrailingJsonLine(string stdout)
    {
        var lines = stdout.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || !trimmed.StartsWith('{')) continue;
            try
            {
                using var _ = JsonDocument.Parse(trimmed);
                return trimmed;
            }
            catch (JsonException) { /* keep looking */ }
        }
        return null;
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";

}
