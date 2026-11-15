using System.Diagnostics;

namespace UnitTests.AgentBridge.Headless.McpProcess;

/// <summary>
/// A throw-away home for a Lunima child process. HOME and the XDG folders (Linux/macOS, where .NET
/// derives every per-user folder from them) and USERPROFILE/APPDATA/LOCALAPPDATA point into a temp
/// directory, so a run never reads or writes the developer's preferences, recent files or user PDKs.
/// Windows resolves known folders from the user profile rather than the environment; there the
/// headless session's private preferences copy is what keeps the user's file untouched.
/// </summary>
internal sealed class LunimaSandbox : IAsyncDisposable
{
    private static readonly TimeSpan DeleteRetryWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DeleteRetryDelay = TimeSpan.FromMilliseconds(50);
    private const int ShortIdLength = 12;

    private LunimaSandbox(string root)
    {
        Root = root;
        var home = Path.Combine(root, "home");
        var folders = new Dictionary<string, string>
        {
            ["HOME"] = home,
            ["USERPROFILE"] = home,
            ["XDG_CONFIG_HOME"] = Path.Combine(home, ".config"),
            ["XDG_DATA_HOME"] = Path.Combine(home, ".local", "share"),
            ["XDG_CACHE_HOME"] = Path.Combine(home, ".cache"),
            ["APPDATA"] = Path.Combine(home, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(home, "AppData", "Local"),
        };
        foreach (var folder in folders.Values)
            Directory.CreateDirectory(folder);
        Environment = folders;
    }

    /// <summary>The sandbox directory; also the child's working directory.</summary>
    public string Root { get; }

    /// <summary>Environment overrides that move every per-user folder into the sandbox.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; }

    /// <summary>Creates an empty sandbox under the system temp directory.</summary>
    public static LunimaSandbox Create() =>
        new(Path.Combine(Path.GetTempPath(), $"lunima-mcp-{Guid.NewGuid().ToString("N")[..ShortIdLength]}"));

    /// <summary>An absolute path for <paramref name="fileName"/> inside the sandbox.</summary>
    public string PathFor(string fileName) => Path.Combine(Root, fileName);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        var elapsed = Stopwatch.StartNew();
        while (Directory.Exists(Root))
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                && OperatingSystem.IsWindows() && elapsed.Elapsed < DeleteRetryWindow)
            {
                // A just-exited child's file handles can linger briefly on Windows.
                await Task.Delay(DeleteRetryDelay);
            }
        }
    }
}
