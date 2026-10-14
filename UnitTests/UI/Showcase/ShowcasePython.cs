using CAP_Core.Export;
using CAP_Core.Export.PythonEnvironmentManager;

namespace UnitTests.UI.Showcase;

/// <summary>
/// Finds a Python interpreter with Pillow for the showcase GIF encoder: the explicit
/// <c>SHOWCASE_PYTHON</c> override first, then a Lunima managed / ground-truth venv, then
/// the usual launcher names on PATH. The answer is cached for the test run.
/// </summary>
internal static class ShowcasePython
{
    private const int ProbeTimeoutMs = 20_000;
    private static readonly string[] PathCandidates = { "python3", "python", "py" };
    private static string? _cached;
    private static bool _probed;

    /// <summary>The first interpreter that can <c>import PIL</c>, or null. Never blocks
    /// the (headless) UI thread — the probes are awaited.</summary>
    public static async Task<string?> FindWithPillowAsync()
    {
        if (_probed) return _cached;
        var candidates = new List<string>();
        var explicitPython = Environment.GetEnvironmentVariable("SHOWCASE_PYTHON");
        if (!string.IsNullOrEmpty(explicitPython)) candidates.Add(explicitPython);
        if (ShowcaseExportedGds.FindGdsFactoryPython() is { } venv) candidates.Add(venv);
        candidates.AddRange(PathCandidates);

        foreach (var python in candidates)
        {
            if (!await HasPillowAsync(python)) continue;
            _cached = python;
            break;
        }
        _probed = true;
        return _cached;
    }

    private static async Task<bool> HasPillowAsync(string python)
    {
        try
        {
            var (exit, _, _) = await UvBootstrapper.RunProcessAsync(
                ProcessLaunchFactory.CreateDefault(), python, new[] { "-c", "import PIL" },
                CancellationToken.None, ProbeTimeoutMs);
            return exit == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
