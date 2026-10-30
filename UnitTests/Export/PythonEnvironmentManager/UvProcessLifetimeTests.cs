using CAP_Core.Export;
using CAP_Core.Export.PythonEnvironmentManager;
using Shouldly;
using UnitTests.Solvers;

namespace UnitTests.Export.PythonEnvironmentManager;

public sealed class UvProcessLifetimeTests
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RootExitsFirst_RetiresDescendantsBeforeSuccessfulReturn(bool inheritPipes)
    {
        await using var child = await ControlledSolverProcess.CreateAsync(OwnedToolProcessScenarios.RootExits(inheritPipes));
        var running = RunAsync(child, CancellationToken.None);
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.exitCode.ShouldBe(0);
            result.output.ShouldContain("fixture");
            child.ShouldHaveExited();
            await child.DescendantsShouldHaveExitedAsync();
        }
        finally { await OwnedToolProcessScenarios.CleanupAsync(child, running); }
    }

    [SkippableFact]
    public async Task ClosedOutputStreams_DoNotCancelTheRootDeadline()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("""
            os.close(1)
            os.close(2)
            time.sleep(60)
            """);
        var running = RunAsync(child, CancellationToken.None);
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.exitCode.ShouldBe(-1);
            result.output.ShouldBeEmpty();
            result.error.ShouldBe("Operation timed out.");
            child.ShouldHaveExited();
        }
        finally { await OwnedToolProcessScenarios.CleanupAsync(child, running); }
    }

    [SkippableFact]
    public async Task CallerCancellation_ThrowsOnlyAfterRootAndDescendantsAreRetired()
    {
        await using var child = await ControlledSolverProcess.CreateAsync(
            OwnedToolProcessScenarios.SpawnDescendant(inheritPipes: true) + "\ntime.sleep(60)");
        using var cancellation = new CancellationTokenSource();
        var running = RunAsync(child, cancellation.Token, timeoutMs: 30_000);
        try
        {
            await OwnedToolProcessScenarios.AwaitDescendantAsync(child);
            cancellation.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(ControlledSolverProcess.GuardTimeout));
            child.ShouldHaveExited();
            await child.DescendantsShouldHaveExitedAsync();
        }
        finally { await OwnedToolProcessScenarios.CleanupAsync(child, running); }
    }

    private static Task<(int exitCode, string output, string error)> RunAsync(
        ControlledSolverProcess child, CancellationToken token, int timeoutMs = 700) =>
        UvBootstrapper.RunProcessAsync(ProcessLaunchFactory.CreateDefault(), child.PythonExecutable,
            ["-u", child.ScriptPath], token, timeoutMs);
}
