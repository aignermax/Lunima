using CAP_Core.Export.Processes;
using System.Runtime.InteropServices;

namespace CAP_Core.Export.PythonEnvironmentManager;

/// <summary>
/// Locates or downloads the <c>uv</c> binary and uses it to create Python virtual
/// environments with a specific Python version. Supports Windows, Linux, and macOS.
/// All subprocesses are launched through <see cref="ProcessLaunchFactory"/> so PATH
/// resolution behaves identically on every OS (CLAUDE.md §1.2).
/// </summary>
public class UvBootstrapper
{
    /// <summary>Default Python version to request when none is specified.</summary>
    public const string DefaultPythonVersion = "3.11";

    /// <summary>
    /// Timeout for uv operations that may download a full CPython toolchain on first
    /// use — 120s is not enough on slow connections, so these get 10 minutes.
    /// </summary>
    public const int LongOperationTimeoutMs = 600_000;

    private static readonly string AppDataDir =
        Path.Combine(Helpers.AppDataFolders.LocalApplicationData, "Lunima");

    private static readonly string ToolsDir = Path.Combine(AppDataDir, "tools");

    private static readonly HttpClient DownloadClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    private readonly ProcessLaunchFactory _launchFactory;

    /// <summary>Directory where managed environments are stored.</summary>
    public static string EnvironmentsBaseDir => Path.Combine(AppDataDir, "envs");

    /// <summary>Initialises the bootstrapper.</summary>
    /// <param name="launchFactory">Factory for cross-platform process launches;
    /// null uses <see cref="ProcessLaunchFactory.CreateDefault"/>.</param>
    public UvBootstrapper(ProcessLaunchFactory? launchFactory = null)
    {
        _launchFactory = launchFactory ?? ProcessLaunchFactory.CreateDefault();
    }

    /// <summary>
    /// Resolves the path to the <c>uv</c> executable: checks PATH first, then the
    /// verified versioned tools directory, then downloads a pinned release if needed.
    /// An existing user-managed PATH installation remains the user's chosen tool.
    /// </summary>
    /// <param name="progress">Receives human-readable status updates.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Absolute path to a working <c>uv</c> binary.</returns>
    /// <exception cref="InvalidOperationException">If uv cannot be found or downloaded.</exception>
    public async Task<string> EnsureUvAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        var pathUv = FindUvOnPath();
        if (pathUv != null)
        {
            progress?.Report($"Found uv at {pathUv}");
            return pathUv;
        }

        var installer = new VerifiedUvInstaller(DownloadClient, ToolsDir);
        var path = await installer.EnsureAsync(UvReleaseCatalog.Current, progress, ct).ConfigureAwait(false);
        progress?.Report($"Verified uv {UvReleaseCatalog.Version} is ready.");
        return path;
    }

    /// <summary>
    /// Creates a new virtual environment at <paramref name="venvPath"/> using
    /// <c>uv venv --python {pythonVersion}</c>. First use may download a full CPython
    /// toolchain, so this runs with <see cref="LongOperationTimeoutMs"/>.
    /// </summary>
    /// <param name="uvPath">Path to the uv binary.</param>
    /// <param name="venvPath">Directory where the venv will be created.</param>
    /// <param name="pythonVersion">Python version string, e.g. "3.11".</param>
    /// <param name="progress">Receives status updates.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">If venv creation fails.</exception>
    public async Task CreateVenvAsync(
        string uvPath,
        string venvPath,
        string pythonVersion,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(venvPath)!);
        progress?.Report($"Creating Python {pythonVersion} venv at {venvPath}...");

        var (exitCode, _, stderr) = await RunProcessAsync(
            _launchFactory, uvPath, new[] { "venv", "--managed-python", "--python", pythonVersion, venvPath },
            ct, LongOperationTimeoutMs);

        if (exitCode != 0)
            throw new InvalidOperationException(
                $"uv venv creation failed (exit {exitCode}): {stderr}");

        progress?.Report("Virtual environment created.");
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private static string? FindUvOnPath()
    {
        var uvName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "uv.exe" : "uv";
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, uvName);
            if (File.Exists(candidate))
                return candidate;
        }

        // Check uv's default self-install location
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var uvDefault = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "uv", "bin", "uv.exe")
            : Path.Combine(home, ".local", "bin", "uv");

        return File.Exists(uvDefault) ? uvDefault : null;
    }

    /// <summary>
    /// Runs a process built via <paramref name="launchFactory"/> and captures its output.
    /// A user cancellation throws <see cref="OperationCanceledException"/>; a pure timeout
    /// returns exit code -1 with a "timed out" error so callers report it as a failure.
    /// </summary>
    internal static async Task<(int exitCode, string output, string error)> RunProcessAsync(
        ProcessLaunchFactory launchFactory,
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken ct,
        int timeoutMs = 120_000)
    {
        if (!launchFactory.TryBuild(fileName, arguments, null, null, out var startInfo, out var buildError))
            throw new InvalidOperationException($"Cannot launch '{fileName}': {buildError}");

        var result = await OwnedProcessCapture.RunAsync(startInfo, TimeSpan.FromMilliseconds(timeoutMs), ct)
            .ConfigureAwait(false);
        return result.TimedOut ? (-1, string.Empty, "Operation timed out.")
            : (result.ExitCode, result.Output, result.Error);
    }
}
