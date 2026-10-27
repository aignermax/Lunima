using CAP_Core.Export;
using Shouldly;
using UnitTests.Solvers;

namespace UnitTests.Export;

public sealed class GdsComparisonProcessLifetimeTests
{
    private const string Success = """
        import json
        with open(sys.argv[3], "w") as report:
            json.dump({"passed":True,"max_deviation_um":0.125,"rms_deviation_um":0.0625,"tolerance_um":0.25}, report)
        print('comparison complete', flush=True)
        os._exit(0)
        """;

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RootExitsFirst_RetiresDescendantsBeforeReturningTheComparison(bool inheritPipes)
    {
        await using var child = await ControlledSolverProcess.CreateAsync(
            OwnedToolProcessScenarios.SpawnDescendant(inheritPipes) + "\n" + Success);
        var running = Service(child, TimeSpan.FromSeconds(5)).CompareAsync(child.ScriptPath, child.ScriptPath, child.ScriptPath);
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Passed.ShouldBeTrue(result.ErrorOutput);
            result.RawOutput.Trim().ShouldBe("comparison complete");
            result.MaxDeviationUm.ShouldBe(0.125);
            child.ShouldHaveExited();
            await child.DescendantsShouldHaveExitedAsync();
        }
        finally { await OwnedToolProcessScenarios.CleanupAsync(child, running); }
    }

    [SkippableFact]
    public async Task CallerCancellation_RetiresRootAndDescendantsBeforeThrowing()
    {
        await using var child = await ControlledSolverProcess.CreateAsync(
            OwnedToolProcessScenarios.SpawnDescendant(true) + "\ntime.sleep(60)");
        using var stop = new CancellationTokenSource();
        var running = Service(child, TimeSpan.FromSeconds(30)).CompareAsync(
            child.ScriptPath, child.ScriptPath, child.ScriptPath, stop.Token);
        try
        {
            await OwnedToolProcessScenarios.AwaitDescendantAsync(child);
            stop.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(
                () => running.WaitAsync(ControlledSolverProcess.GuardTimeout));
            child.ShouldHaveExited();
            await child.DescendantsShouldHaveExitedAsync();
        }
        finally { await OwnedToolProcessScenarios.CleanupAsync(child, running); }
    }

    [SkippableFact]
    public async Task ClosedOutputStreams_StillTimeOutAndRetireTheLiveRoot()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("os.close(1)\nos.close(2)\ntime.sleep(60)");
        var running = Service(child).CompareAsync(child.ScriptPath, child.ScriptPath, child.ScriptPath);
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Passed.ShouldBeFalse();
            result.ErrorOutput.ShouldStartWith("Timeout after ");
            child.ShouldHaveExited();
        }
        finally { await OwnedToolProcessScenarios.CleanupAsync(child, running); }
    }

    private static GdsCoordinateComparisonService Service(ControlledSolverProcess child, TimeSpan? timeout = null)
    {
        var service = new GdsCoordinateComparisonService(null,
            new CoordinateExecutionOptions(timeout ?? OwnedToolProcessScenarios.Deadline));
        service.SetCustomPythonPath(child.PythonExecutable);
        return service;
    }
}
