namespace CAP_Core.Export.Processes;

/// <summary>Detects installed packages without executing their initialization code.</summary>
public static class PythonPackageProbe
{
    /// <summary>Deadline for interpreter startup and package metadata lookup.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Builds a probe for a top-level Python module, including unpackaged source installs.</summary>
    public static string[] Arguments(string moduleName) => new[]
    {
        "-c",
        "import importlib.util, importlib.metadata, sys\n" +
        "name = sys.argv[1]\n" +
        "if importlib.util.find_spec(name) is None: sys.exit(1)\n" +
        "try: print(importlib.metadata.version(name))\n" +
        "except importlib.metadata.PackageNotFoundError: print('unknown')",
        moduleName
    };

    /// <summary>Checks package presence; a timeout is inconclusive, not a missing package.</summary>
    public static async Task<string?> GetVersionAsync(ProcessLaunchFactory factory, string pythonPath,
        string moduleName, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!factory.TryBuild(pythonPath, Arguments(moduleName), null, null, out var info, out _)) return null;
        var deadline = timeout ?? DefaultTimeout;
        var result = await OwnedProcessCapture.RunAsync(info, deadline, cancellationToken);
        if (result.TimedOut)
            throw new TimeoutException(FormattableString.Invariant(
                $"Python found at '{pythonPath}', but checking {moduleName} timed out after {deadline.TotalSeconds:g} s. Retry or check this interpreter in Settings → Python."));
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output) ? result.Output.Trim() : null;
    }
}
