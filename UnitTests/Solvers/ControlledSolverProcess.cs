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

    internal async Task WaitForRootExitAsync()
    {
        await WaitForStartedAsync();
        using var deadline = new CancellationTokenSource(GuardTimeout);
        await WaitForTerminationAsync(_pidFile, deadline.Token);
    }

    internal async Task WaitForStartedAsync()
    {
        using var deadline = new CancellationTokenSource(GuardTimeout);
        while (!File.Exists(_pidFile) || !int.TryParse(await File.ReadAllTextAsync(_pidFile, deadline.Token), out _))
            await Task.Delay(10, deadline.Token);
    }

    internal async Task DescendantsShouldHaveExitedAsync()
    {
        foreach (var pidFile in Directory.GetFiles(_directory, "*.pid").Where(path => path != _pidFile))
        {
            using var deadline = new CancellationTokenSource(GuardTimeout);
            await WaitForTerminationAsync(pidFile, deadline.Token);
        }
    }

    private static async Task WaitForTerminationAsync(string pidFile, CancellationToken token)
    {
        while (true)
        {
            using var process = FindChild(pidFile);
            if (process == null || process.HasExited || await IsZombieAsync(process.Id, token)) return;
            await Task.Delay(10, token);
        }
    }

    private static async Task<bool> IsZombieAsync(int pid, CancellationToken token)
    {
        if (OperatingSystem.IsWindows()) return false;
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var stat = await File.ReadAllTextAsync($"/proc/{pid}/stat", token);
                var stateOffset = stat.LastIndexOf(')') + 2;
                return stateOffset >= 2 && stateOffset < stat.Length && stat[stateOffset] is 'Z' or 'X';
            }
            catch (FileNotFoundException) { return true; }
            catch (DirectoryNotFoundException) { return true; }
        }
        var factory = ProcessLaunchFactory.CreateDefault();
        factory.TryBuild("ps", new[] { "-o", "stat=", "-p", pid.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            null, null, out var info, out var error).ShouldBeTrue(error);
        info.RedirectStandardOutput = true;
        using var probe = new Process { StartInfo = info };
        probe.Start();
        var output = probe.StandardOutput.ReadToEndAsync(token);
        try
        {
            await probe.WaitForExitAsync(token);
            var state = (await output).Trim();
            return state.Length == 0 || state.StartsWith('Z');
        }
        finally { await KillAndReapAsync(probe); }
    }

    public async ValueTask DisposeAsync()
    {
        await TerminateChildrenAsync();
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try { Directory.Delete(_directory, recursive: true); return; }
            catch (IOException error) when (OperatingSystem.IsWindows()
                && (error.HResult & 0xffff) is 32 or 33 && elapsed.Elapsed < GuardTimeout)
            {
                // Exited children are verified separately. Windows readers can briefly
                // retain a sharing lock; retry only that condition, with a bounded deadline.
                await Task.Delay(25);
            }
        }
    }

    internal async Task TerminateChildrenAsync()
    {
        foreach (var pidFile in Directory.GetFiles(_directory, "*.pid"))
        {
            using var child = FindChild(pidFile);
            if (child != null)
            {
                using var deadline = new CancellationTokenSource(GuardTimeout);
                if (!await IsZombieAsync(child.Id, deadline.Token)) await KillAndReapAsync(child);
            }
        }
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
