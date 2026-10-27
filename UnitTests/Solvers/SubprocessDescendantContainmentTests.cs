using CAP.Avalonia.Services.Solvers;
using Shouldly;
using Xunit;

namespace UnitTests.Solvers;

/// <summary>Ordinary descendants remain owned after the original solver process exits.</summary>
public sealed class SubprocessDescendantContainmentTests
{
    [SkippableFact]
    public async Task Timeout_AfterRootExit_TerminatesDescendantHoldingOutputPipes()
    {
        var child = await CreateRootExitingFirstAsync(inheritOutput: true);
        using var cancellation = new CancellationTokenSource();
        var run = SubprocessJsonRunner.RunAsync(child.StartInfo, "{}", TimeSpan.FromSeconds(5), cancellation.Token);
        try
        {
            await child.WaitForRootExitAsync();
            run.IsCompleted.ShouldBeFalse("the descendant still holds the inherited output pipes");
            var result = await run.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Outcome.ShouldBe(SubprocessJsonRunner.Outcome.TimedOut);
            child.ShouldHaveExited();
            await child.DescendantsShouldHaveExitedAsync();
        }
        finally { await CleanupAsync(child, cancellation, run); }
    }

    [SkippableFact]
    public async Task Cancellation_AfterRootExit_TerminatesDescendantHoldingOutputPipes()
    {
        var child = await CreateRootExitingFirstAsync(inheritOutput: true);
        using var cancellation = new CancellationTokenSource();
        var run = SubprocessJsonRunner.RunAsync(child.StartInfo, "{}", TimeSpan.FromSeconds(30), cancellation.Token);
        try
        {
            await child.WaitForRootExitAsync();
            run.IsCompleted.ShouldBeFalse();
            cancellation.Cancel();
            var result = await run.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Outcome.ShouldBe(SubprocessJsonRunner.Outcome.Cancelled);
            child.ShouldHaveExited();
            await child.DescendantsShouldHaveExitedAsync();
        }
        finally { await CleanupAsync(child, cancellation, run); }
    }

    [SkippableFact]
    public async Task Completion_AfterRootExit_TerminatesDescendantWithIndependentOutput()
    {
        var child = await CreateRootExitingFirstAsync(inheritOutput: false);
        using var cancellation = new CancellationTokenSource();
        var run = SubprocessJsonRunner.RunAsync(child.StartInfo, "{}", TimeSpan.FromSeconds(5), cancellation.Token);
        try
        {
            var result = await run.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Outcome.ShouldBe(SubprocessJsonRunner.Outcome.Completed);
            result.ExitCode.ShouldBe(0);
            SubprocessJsonRunner.ExtractTrailingJsonLine(result.Stdout).ShouldBe("{\"complete\":true}");
            child.ShouldHaveExited();
            await child.DescendantsShouldHaveExitedAsync();
        }
        finally { await CleanupAsync(child, cancellation, run); }
    }

    private static Task<ControlledSolverProcess> CreateRootExitingFirstAsync(bool inheritOutput) =>
        ControlledSolverProcess.CreateAsync($$"""
            import subprocess
            sys.stdin.read()
            descendant = subprocess.Popen(
                [sys.executable, "-u", "-c", "import time; time.sleep(20)"],
                stdin=subprocess.DEVNULL,
                stdout={{(inheritOutput ? "None" : "subprocess.DEVNULL")}},
                stderr={{(inheritOutput ? "None" : "subprocess.DEVNULL")}})
            publish_fixture_signal("descendant.pid", descendant.pid)
            print('{"complete":true}', flush=True)
            os._exit(0)
            """);

    private static async Task CleanupAsync(ControlledSolverProcess child,
        CancellationTokenSource cancellation, Task<SubprocessJsonRunner.RunResult> run)
    {
        cancellation.Cancel();
        // The fixture tracks descendant PIDs separately: a broken runner must not leak
        // the reparented child, including when an assertion or the test guard fails.
        await child.DisposeAsync();
        try { await run.WaitAsync(ControlledSolverProcess.GuardTimeout); }
        catch (Exception) { /* Preserve the assertion failure after owned child cleanup. */ }
    }
}
