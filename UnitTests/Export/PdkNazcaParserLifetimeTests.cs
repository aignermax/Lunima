using CAP_Core.Export;
using Shouldly;
using UnitTests.Solvers;

namespace UnitTests.Export;

public sealed class PdkNazcaParserLifetimeTests
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RootExitsFirst_RetiresDescendantsBeforeReturningParsedData(bool inheritPipes)
    {
        await using var child = await ControlledSolverProcess.CreateAsync(OwnedToolProcessScenarios.RootExits(inheritPipes));
        var running = Parser(child, TimeSpan.FromSeconds(5)).ParseAsync("fixture");
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Name.ShouldBe("fixture");
            result.Components.ShouldBeEmpty();
            child.ShouldHaveExited();
            await child.DescendantsShouldHaveExitedAsync();
        }
        finally { await OwnedToolProcessScenarios.CleanupAsync(child, running); }
    }

    [SkippableFact]
    public async Task CallerCancellation_ThrowsOnlyAfterRootAndDescendantsAreRetired()
    {
        await using var child = await ControlledSolverProcess.CreateAsync(
            OwnedToolProcessScenarios.SpawnDescendant(inheritPipes: true) + "\ntime.sleep(60)");
        using var cancellation = new CancellationTokenSource();
        var running = Parser(child, TimeSpan.FromSeconds(30)).ParseAsync("fixture", cancellationToken: cancellation.Token);
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

    [SkippableFact]
    public async Task ClosedOutputStreams_StillReportTheParserTimeout()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("""
            os.close(1)
            os.close(2)
            time.sleep(60)
            """);
        var running = Parser(child).ParseAsync("fixture");
        try
        {
            var error = await Should.ThrowAsync<PdkParserException>(() => running.WaitAsync(ControlledSolverProcess.GuardTimeout));
            error.Message.ShouldStartWith("parse_pdk.py did not finish within");
            child.ShouldHaveExited();
        }
        finally { await OwnedToolProcessScenarios.CleanupAsync(child, running); }
    }

    [SkippableFact]
    public async Task LargeStderr_DrainsWhileKeepingTheJsonContract()
    {
        await using var child = await ControlledSolverProcess.CreateAsync(
            "sys.stderr.write('e' * 1048576)\nsys.stderr.flush()\n" + OwnedToolProcessScenarios.JsonOutput);
        var running = Parser(child, TimeSpan.FromSeconds(5)).ParseAsync("fixture");
        try
        {
            (await running.WaitAsync(ControlledSolverProcess.GuardTimeout)).Name.ShouldBe("fixture");
            child.ShouldHaveExited();
        }
        finally { await OwnedToolProcessScenarios.CleanupAsync(child, running); }
    }

    private static PdkNazcaParserService Parser(ControlledSolverProcess child, TimeSpan? timeout = null) =>
        new(child.PythonExecutable, child.ScriptPath, timeout ?? OwnedToolProcessScenarios.Deadline);
}
