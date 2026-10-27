using UnitTests.Solvers;

namespace UnitTests.Export;

internal static class OwnedToolProcessScenarios
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(700);
    internal const string JsonOutput = """
        print('{"name":"fixture","components":[]}', flush=True)
        os._exit(0)
        """;

    internal static string RootExits(bool inheritPipes) => SpawnDescendant(inheritPipes) + "\n" + JsonOutput;

    internal static string SpawnDescendant(bool inheritPipes) => $$"""
        import subprocess
        descendant = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(60)"],
            stdout={{(inheritPipes ? "None" : "subprocess.DEVNULL")}},
            stderr={{(inheritPipes ? "None" : "subprocess.DEVNULL")}})
        with open(os.path.join(os.path.dirname(__file__), "descendant.pid"), "w") as pid_file:
            pid_file.write(str(descendant.pid))
        """;

    internal static async Task AwaitDescendantAsync(ControlledSolverProcess child)
    {
        using var deadline = new CancellationTokenSource(ControlledSolverProcess.GuardTimeout);
        var path = Path.Combine(Path.GetDirectoryName(child.ScriptPath)!, "descendant.pid");
        while (!File.Exists(path) || !int.TryParse(await File.ReadAllTextAsync(path, deadline.Token), out _))
            await Task.Delay(10, deadline.Token);
    }

    internal static async Task CleanupAsync(ControlledSolverProcess child, Task running)
    {
        // Also rescue the intentionally defective baseline after its guard fails.
        // Observe the actual invocation before its process pipes are discarded.
        await child.TerminateChildrenAsync();
        try { await running.WaitAsync(ControlledSolverProcess.GuardTimeout); }
        catch when (running.IsCompleted) { }
    }
}
