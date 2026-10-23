using CAP.Avalonia.Services.Solvers;
using CAP_Core.Solvers.ModeSolver;
using Shouldly;
using Xunit;

namespace UnitTests.Solvers.ModeSolver;

/// <summary>Verifies the mode-solver service uses bounded subprocess lifetimes.</summary>
public class PythonModeSolverLifecycleTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(700);

    [SkippableFact]
    public async Task SolveAsync_OutputClosesBeforeProcessExit_ReportsTimeoutAndReaps()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("""
            sys.stdin.read()
            os.close(1)
            os.close(2)
            time.sleep(60)
            """);
        var service = new PythonModeSolverService(child.PythonExecutable, child.ScriptPath, Deadline);

        var result = await service.SolveAsync(new ModeSolverRequest())
            .WaitAsync(ControlledSolverProcess.GuardTimeout);

        result.Success.ShouldBeFalse();
        result.Error.ShouldBe("Mode solver timed out after 1 s.");
        child.ShouldHaveExited();
    }

    [SkippableFact]
    public async Task SolveAsync_CancelWithBlockedRequest_ReportsCancellationAndReaps()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("time.sleep(60)");
        var service = new PythonModeSolverService(child.PythonExecutable, child.ScriptPath);
        var request = new ModeSolverRequest { Wavelengths = Enumerable.Repeat(1.55, 250000).ToArray() };
        using var cancellation = new CancellationTokenSource(Deadline);

        var result = await service.SolveAsync(request, cancellation.Token)
            .WaitAsync(ControlledSolverProcess.GuardTimeout);

        result.Success.ShouldBeFalse();
        result.Error.ShouldBe("Mode solve was cancelled.");
        child.ShouldHaveExited();
    }

    [SkippableFact]
    public async Task SolveAsync_FailedBackendWithNonzeroExit_PreservesJsonErrorAndStderr()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("""
            sys.stdin.read()
            print("backend diagnostic", file=sys.stderr, flush=True)
            print("solver log chatter", flush=True)
            print('{"success":false,"error":"Install the EMpy backend","missing_backend":"EMpy"}', flush=True)
            os._exit(7)
            """);
        var service = new PythonModeSolverService(child.PythonExecutable, child.ScriptPath);

        var result = await service.SolveAsync(new ModeSolverRequest())
            .WaitAsync(ControlledSolverProcess.GuardTimeout);

        result.Success.ShouldBeFalse();
        result.Error.ShouldBe("Install the EMpy backend");
        result.MissingBackend.ShouldBe("EMpy");
        result.RawStderr.ShouldContain("backend diagnostic");
        child.ShouldHaveExited();
    }
}
