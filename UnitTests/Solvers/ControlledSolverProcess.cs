using System.Diagnostics;
using System.Text.Json;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Solvers;

internal sealed class ControlledSolverProcess : IAsyncDisposable
{
    internal static readonly TimeSpan GuardTimeout = TimeSpan.FromSeconds(8);
    private readonly string _directory;
    private readonly string _pidFile;
    internal ProcessStartInfo StartInfo { get; }
    internal string PythonExecutable => StartInfo.FileName;
    internal string ScriptPath { get; }

    private ControlledSolverProcess(string python, string body)
    {
        _directory = Path.Combine(Path.GetTempPath(), $"solver-lifecycle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _pidFile = Path.Combine(_directory, "child.pid");
        ScriptPath = Path.Combine(_directory, "child.py");
        File.WriteAllText(ScriptPath, $$"""
            import os, sys, time, threading
            threading.Timer(20, lambda: os._exit(99)).start()
            with open({{JsonSerializer.Serialize(_pidFile)}}, "w") as pid_file:
                pid_file.write(str(os.getpid()))
            """ + "\n" + body);
        var factory = ProcessLaunchFactory.CreateDefault();
        factory.TryBuild(python, new[] { "-u", ScriptPath }, _directory, null,
            out var info, out var error).ShouldBeTrue(error);
        StartInfo = info;
    }

    internal static async Task<ControlledSolverProcess> CreateAsync(string body)
    {
        var python = await FindPythonAsync();
        Skip.If(python == null, "A Python interpreter is required for subprocess lifecycle regressions.");
        return new ControlledSolverProcess(python!, body);
    }

    internal void ShouldHaveExited()
    {
        File.Exists(_pidFile).ShouldBeTrue("the controlled child must have started");
        using var child = FindChild(_pidFile);
        (child == null || child.HasExited).ShouldBeTrue("the runner must reap its child before returning");
    }

    internal async Task DescendantsShouldHaveExitedAsync()
    {
        foreach (var pidFile in Directory.GetFiles(_directory, "*.pid").Where(path => path != _pidFile))
        {
            using var child = FindChild(pidFile);
            if (child != null)
                await child.WaitForExitAsync().WaitAsync(GuardTimeout);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var pidFile in Directory.GetFiles(_directory, "*.pid"))
        {
            using var child = FindChild(pidFile);
            if (child != null) await KillAndReapAsync(child);
        }
        Directory.Delete(_directory, recursive: true);
    }

    private static Process? FindChild(string pidFile)
    {
        if (!int.TryParse(File.ReadAllText(pidFile), out var pid))
            return null;
        try { return Process.GetProcessById(pid); }
        catch (ArgumentException) { return null; }
    }

    private static async Task<string?> FindPythonAsync()
    {
        var factory = ProcessLaunchFactory.CreateDefault();
        foreach (var name in OperatingSystem.IsWindows()
                     ? new[] { "python", "python3", "py" } : new[] { "python3", "python" })
        {
            var arguments = name == "py"
                ? new[] { "-3", "-c", "import sys; print(sys.executable)" }
                : new[] { "-c", "import sys; print(sys.executable)" };
            factory.TryBuild(name, arguments, null, null, out var info, out _);
            info.RedirectStandardOutput = true;
            using var process = new Process { StartInfo = info };
            var started = false;
            try
            {
                started = process.Start();
                var output = process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                var executable = (await output.WaitAsync(GuardTimeout)).Trim();
                if (process.ExitCode == 0 && File.Exists(executable)) return executable;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or TimeoutException)
            { }
            finally
            {
                if (started) await KillAndReapAsync(process);
            }
        }
        return null;
    }

    private static async Task KillAndReapAsync(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        await process.WaitForExitAsync().WaitAsync(GuardTimeout);
    }
}
