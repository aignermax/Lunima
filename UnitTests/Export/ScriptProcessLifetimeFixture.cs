using Shouldly;
using UnitTests.Solvers;

namespace UnitTests.Export;

internal static class ScriptProcessLifetimeFixture
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);
    internal const string PreviewJson = "{\"success\":true,\"bbox\":{\"xmin\":0,\"ymin\":0,\"xmax\":2,\"ymax\":3}}";

    internal static string DescendantBody(bool inheritOutput) => $$"""
        import subprocess
        descendant = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(20)"],
            stdin=subprocess.DEVNULL,
            stdout={{(inheritOutput ? "None" : "subprocess.DEVNULL")}},
            stderr={{(inheritOutput ? "None" : "subprocess.DEVNULL")}})
        publish_fixture_signal(os.path.join(os.path.dirname(os.path.abspath(__file__)), "descendant.pid"), descendant.pid)
        """;

    internal static async Task<string> ReadMarkerAsync(ControlledSolverProcess child, string name)
    {
        var path = Path.Combine(Path.GetDirectoryName(child.ScriptPath)!, name);
        using var deadline = new CancellationTokenSource(ControlledSolverProcess.GuardTimeout);
        while (true)
        {
            if (File.Exists(path))
            {
                var value = await File.ReadAllTextAsync(path, deadline.Token);
                if (value.Length > 0) return value;
            }
            await Task.Delay(10, deadline.Token);
        }
    }

    internal static async Task CleanupAsync(ControlledSolverProcess child, Task running, CancellationTokenSource stop)
    {
        stop.Cancel();
        await child.TerminateChildrenAsync();
        try { await running.WaitAsync(ControlledSolverProcess.GuardTimeout); }
        catch (Exception) when (running.IsCompleted) { }
    }

    internal static async Task AssertDescendantRetiredAsync(ControlledSolverProcess child)
    {
        (await ReadMarkerAsync(child, "descendant.pid")).ShouldNotBeNullOrWhiteSpace();
        child.ShouldHaveExited();
        await child.DescendantsShouldHaveExitedAsync();
    }
}
