using CAP.Avalonia.Services.Solvers;
using Shouldly;
using Xunit;

namespace UnitTests.Solvers;

/// <summary>Exercises real redirected pipes and child lifetimes without solver dependencies.</summary>
public class SubprocessJsonRunnerTests
{
    private const int PayloadLength = 1024 * 1024;
    private static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(700);

    [SkippableFact]
    public async Task RunAsync_OutputBeforeReadingLargeInput_DrainsBothStreams()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("""
            sys.stdout.write("o" * 1048576 + "\n")
            sys.stderr.write("e" * 1048576 + "\n")
            payload = sys.stdin.read()
            print('{"received":' + str(len(payload)) + '}')
            os._exit(0)
            """);

        var result = await RunGuardedAsync(child, new string('i', PayloadLength), TimeSpan.FromSeconds(5));

        result.Outcome.ShouldBe(SubprocessJsonRunner.Outcome.Completed);
        result.ExitCode.ShouldBe(0);
        SubprocessJsonRunner.ExtractTrailingJsonLine(result.Stdout).ShouldBe("{\"received\":1048576}");
        result.Stderr.TrimEnd().Length.ShouldBe(PayloadLength);
        child.ShouldHaveExited();
    }

    [SkippableFact]
    public async Task RunAsync_ChildNeverReadsInput_DeadlineKillsAndReaps()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("time.sleep(60)");

        var result = await RunGuardedAsync(child, new string('i', PayloadLength), Deadline);

        result.Outcome.ShouldBe(SubprocessJsonRunner.Outcome.TimedOut);
        result.ExitCode.ShouldBe(-1);
        result.Stdout.ShouldBeEmpty();
        result.Stderr.ShouldBeEmpty();
        child.ShouldHaveExited();
    }

    [SkippableFact]
    public async Task RunAsync_CancelWhileInputBlocked_KillsAndReaps()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("time.sleep(60)");
        using var cancellation = new CancellationTokenSource(Deadline);

        var result = await RunGuardedAsync(child, new string('i', PayloadLength),
            TimeSpan.FromMinutes(1), cancellation.Token);

        result.Outcome.ShouldBe(SubprocessJsonRunner.Outcome.Cancelled);
        child.ShouldHaveExited();
    }

    [SkippableFact]
    public async Task RunAsync_OutputStreamsCloseBeforeExit_DeadlineStillApplies()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("""
            os.close(1)
            os.close(2)
            time.sleep(60)
            """);

        var result = await RunGuardedAsync(child, string.Empty, Deadline);

        result.Outcome.ShouldBe(SubprocessJsonRunner.Outcome.TimedOut);
        child.ShouldHaveExited();
    }

    [SkippableFact]
    public async Task RunAsync_ChildClosesInput_ReapsBeforePropagatingWriteFailure()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("""
            os.close(0)
            time.sleep(60)
            """);

        await Should.ThrowAsync<IOException>(() => RunGuardedAsync(child,
            new string('i', PayloadLength), TimeSpan.FromSeconds(5)));

        child.ShouldHaveExited();
    }

    [SkippableFact]
    public async Task RunAsync_CancelWithDescendant_KillsProcessTree()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("""
            import subprocess
            descendant = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(20)"])
            with open("descendant.pid", "w") as pid_file:
                pid_file.write(str(descendant.pid))
            print("ready", file=sys.stderr, flush=True)
            time.sleep(60)
            """);
        using var cancellation = new CancellationTokenSource();

        var result = await SubprocessJsonRunner.RunAsync(child.StartInfo, string.Empty,
                TimeSpan.FromSeconds(5), cancellation.Token, _ => cancellation.Cancel())
            .WaitAsync(ControlledSolverProcess.GuardTimeout);

        result.Outcome.ShouldBe(SubprocessJsonRunner.Outcome.Cancelled);
        child.ShouldHaveExited();
        await child.DescendantsShouldHaveExitedAsync();
    }

    private static Task<SubprocessJsonRunner.RunResult> RunGuardedAsync(
        ControlledSolverProcess child, string payload, TimeSpan timeout, CancellationToken ct = default) =>
        SubprocessJsonRunner.RunAsync(child.StartInfo, payload, timeout, ct)
            .WaitAsync(ControlledSolverProcess.GuardTimeout);
}
