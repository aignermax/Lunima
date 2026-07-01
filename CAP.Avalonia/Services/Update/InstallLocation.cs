using System.Diagnostics;

namespace CAP.Avalonia.Services.Update;

/// <summary>
/// Describes where the running application is installed, so an updater can replace it and relaunch.
/// </summary>
public sealed class InstallLocation
{
    /// <summary>The directory or bundle to replace — the <c>.app</c> on macOS, the install directory on Windows/Linux.</summary>
    public string Root { get; }

    /// <summary>Full path to the executable, used to relaunch on Windows/Linux and to locate the bundle on macOS.</summary>
    public string ExecutablePath { get; }

    /// <summary>PID of the currently running process; the updater waits for it to exit before swapping.</summary>
    public int ProcessId { get; }

    /// <summary>Initializes a new <see cref="InstallLocation"/>.</summary>
    public InstallLocation(string root, string executablePath, int processId)
    {
        Root = root;
        ExecutablePath = executablePath;
        ProcessId = processId;
    }

    /// <summary>
    /// Resolves the install location of the running process, or null if it can't be determined
    /// (e.g. launched via <c>dotnet run</c> from a build output rather than an installed app).
    /// On macOS the root is the enclosing <c>*.app</c> bundle; on Windows/Linux it is the
    /// executable's directory.
    /// </summary>
    public static InstallLocation? Resolve()
    {
        string? exe;
        try
        {
            exe = Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            return null;

        var pid = Environment.ProcessId;

        if (OperatingSystem.IsMacOS())
        {
            var bundle = FindEnclosingAppBundle(exe);
            return bundle is null ? null : new InstallLocation(bundle, exe, pid);
        }

        var dir = Path.GetDirectoryName(exe);
        return string.IsNullOrEmpty(dir) ? null : new InstallLocation(dir, exe, pid);
    }

    /// <summary>
    /// Walks up from <paramref name="executablePath"/> to the nearest enclosing <c>*.app</c>
    /// bundle directory, or null if the executable is not inside a bundle.
    /// </summary>
    internal static string? FindEnclosingAppBundle(string executablePath)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(executablePath) ?? executablePath);
        while (dir is not null)
        {
            if (dir.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }
}
