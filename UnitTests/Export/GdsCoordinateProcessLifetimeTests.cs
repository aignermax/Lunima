using System.Collections.Immutable;
using CAP_Core.Export;
using Shouldly;
using UnitTests.Solvers;

namespace UnitTests.Export;

public sealed class GdsCoordinateProcessLifetimeTests
{
    private const string WriteExtraction = """
        with open(sys.argv[2], "w") as output:
            output.write('{"database_unit":1e-9,"cells":{}}')
        os._exit(0)
        """;

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Extraction_RootExitsFirst_RetiresDescendantsBeforePublishingJson(bool inheritPipes)
    {
        await using var child = await ControlledSolverProcess.CreateAsync(
            OwnedToolProcessScenarios.SpawnDescendant(inheritPipes) + "\n" + WriteExtraction);
        var running = Extractor(child, TimeSpan.FromSeconds(5)).ExtractAsync(child.ScriptPath, OutputPath(child));
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.JsonContent.ShouldBe("{\"database_unit\":1e-9,\"cells\":{}}");
            child.ShouldHaveExited();
            await child.DescendantsShouldHaveExitedAsync();
        }
        finally { await OwnedToolProcessScenarios.CleanupAsync(child, running); }
    }

    [SkippableFact]
    public async Task Extraction_CallerCancellation_PreservesFailureContractAfterRetiringAllChildren()
    {
        await using var child = await ControlledSolverProcess.CreateAsync(
            OwnedToolProcessScenarios.SpawnDescendant(true) + "\ntime.sleep(60)");
        using var stop = new CancellationTokenSource();
        var running = Extractor(child, TimeSpan.FromSeconds(30))
            .ExtractAsync(child.ScriptPath, OutputPath(child), stop.Token);
        try
        {
            await OwnedToolProcessScenarios.AwaitDescendantAsync(child);
            stop.Cancel();
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldBe("Extraction was cancelled.");
            child.ShouldHaveExited();
            await child.DescendantsShouldHaveExitedAsync();
        }
        finally { await OwnedToolProcessScenarios.CleanupAsync(child, running); }
    }

    [SkippableFact]
    public async Task Extraction_ClosedOutputStreams_StillBoundTheLiveRoot()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("os.close(1)\nos.close(2)\ntime.sleep(60)");
        var running = Extractor(child).ExtractAsync(child.ScriptPath, OutputPath(child));
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldStartWith("Timeout after ");
            child.ShouldHaveExited();
        }
        finally { await OwnedToolProcessScenarios.CleanupAsync(child, running); }
    }

    [SkippableFact]
    public async Task VersionProbe_BlockedProcess_IsBoundedAndRetired()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("time.sleep(60)");
        var running = Extractor(child, probe: true).IsPythonAvailableAsync();
        try
        {
            (await running.WaitAsync(ControlledSolverProcess.GuardTimeout)).ShouldBeFalse();
            child.ShouldHaveExited();
        }
        finally { await OwnedToolProcessScenarios.CleanupAsync(child, running); }
    }

    [SkippableFact]
    public async Task VersionProbe_LargeOutput_IsDrainedWhileWaitingForExit()
    {
        await using var child = await ControlledSolverProcess.CreateAsync(
            "sys.stdout.write('v' * 1048576)\nsys.stdout.flush()\nos._exit(0)");
        var running = Extractor(child, TimeSpan.FromSeconds(5), probe: true).IsPythonAvailableAsync();
        try
        {
            (await running.WaitAsync(ControlledSolverProcess.GuardTimeout)).ShouldBeTrue();
            child.ShouldHaveExited();
        }
        finally { await OwnedToolProcessScenarios.CleanupAsync(child, running); }
    }

    private static GdsCoordinateExtractor Extractor(
        ControlledSolverProcess child, TimeSpan? timeout = null, bool probe = false)
    {
        var options = new CoordinateExecutionOptions(timeout ?? OwnedToolProcessScenarios.Deadline,
            child.ScriptPath, probe ? ImmutableArray.Create("-u", child.ScriptPath) : default);
        var service = new GdsCoordinateExtractor(null, options);
        service.SetCustomPythonPath(child.PythonExecutable);
        return service;
    }

    private static string OutputPath(ControlledSolverProcess child) =>
        Path.Combine(Path.GetDirectoryName(child.ScriptPath)!, "coordinates.json");
}
