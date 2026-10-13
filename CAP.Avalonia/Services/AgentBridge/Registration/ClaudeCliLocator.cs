namespace CAP.Avalonia.Services.AgentBridge.Registration;

/// <summary>
/// Finds the Claude Code CLI (<c>claude</c>) as an absolute path, so it can be launched through
/// <see cref="CAP_Core.Export.ProcessLaunchFactory"/> (which honours a rooted path verbatim).
/// The PATH is searched first — a working PATH always wins — then the locations Claude Code's
/// installers use, because a GUI app started from Finder/Dock or an installer custom action
/// often runs without the user's shell PATH. On Windows the npm shim is <c>claude.cmd</c>,
/// which a plain PATH lookup of <c>claude</c> would not find.
/// </summary>
public sealed class ClaudeCliLocator
{
    private const string BaseName = "claude";

    private readonly Func<string, bool> _fileExists;
    private readonly Func<string?> _pathVariable;
    private readonly Func<Environment.SpecialFolder, string> _folder;
    private readonly bool _isWindows;
    private readonly bool _isMacOs;

    /// <summary>Creates a locator for the current machine.</summary>
    /// <param name="augmentPath">Turns the raw PATH into the one child processes get
    /// (e.g. <c>ProcessLaunchFactory.Prober.AugmentedPath</c>); identity when null.</param>
    public ClaudeCliLocator(Func<string?, string>? augmentPath = null)
        : this(File.Exists,
               () => augmentPath?.Invoke(Environment.GetEnvironmentVariable("PATH"))
                     ?? Environment.GetEnvironmentVariable("PATH"),
               Environment.GetFolderPath,
               OperatingSystem.IsWindows(),
               OperatingSystem.IsMacOS())
    {
    }

    /// <summary>Creates a locator over an injected file system view (tests).</summary>
    public ClaudeCliLocator(
        Func<string, bool> fileExists,
        Func<string?> pathVariable,
        Func<Environment.SpecialFolder, string> folder,
        bool isWindows,
        bool isMacOs)
    {
        _fileExists = fileExists;
        _pathVariable = pathVariable;
        _folder = folder;
        _isWindows = isWindows;
        _isMacOs = isMacOs;
    }

    /// <summary>The absolute path of the <c>claude</c> executable, or null when it is not installed.</summary>
    public string? Locate() => Candidates().FirstOrDefault(_fileExists);

    /// <summary>Every path probed, in order: PATH entries first, then the installers' default locations.</summary>
    public IEnumerable<string> Candidates()
    {
        foreach (var directory in PathDirectories())
            foreach (var name in FileNames())
                yield return Path.Combine(directory, name);
        foreach (var path in WellKnownPaths())
            yield return path;
    }

    private IEnumerable<string> PathDirectories() =>
        (_pathVariable() ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Path.IsPathRooted);

    private IEnumerable<string> FileNames() =>
        _isWindows ? new[] { BaseName + ".exe", BaseName + ".cmd" } : new[] { BaseName };

    private IEnumerable<string> WellKnownPaths()
    {
        var home = _folder(Environment.SpecialFolder.UserProfile);
        if (_isWindows)
        {
            yield return Path.Combine(home, ".local", "bin", BaseName + ".exe");
            yield return Path.Combine(_folder(Environment.SpecialFolder.ApplicationData), "npm", BaseName + ".cmd");
            yield break;
        }

        yield return Path.Combine(home, ".local", "bin", BaseName);
        yield return Path.Combine(home, ".claude", "local", BaseName);
        yield return Path.Combine(home, ".npm-global", "bin", BaseName);
        if (_isMacOs)
            yield return Path.Combine("/opt", "homebrew", "bin", BaseName);
        yield return Path.Combine("/usr", "local", "bin", BaseName);
    }
}
