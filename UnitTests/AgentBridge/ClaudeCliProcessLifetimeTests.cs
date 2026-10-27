using System.Diagnostics;
using CAP.Avalonia.Services.AgentBridge.Registration;
using Shouldly;
using UnitTests.Solvers;

namespace UnitTests.AgentBridge;

public sealed class ClaudeCliProcessLifetimeTests
{
    [Fact]
    public async Task PreCanceledRunner_RejectsAdmissionBeforeAttemptingExecutableLaunch()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var info = new ProcessStartInfo(Path.Combine(Path.GetTempPath(), $"missing-cli-{Guid.NewGuid():N}"))
            { UseShellExecute = false };
        await Should.ThrowAsync<OperationCanceledException>(() => CliProcessRunner.RunAsync(info, TimeSpan.FromSeconds(5), stop.Token));
    }

    [SkippableFact]
    public async Task CallerCancellation_ReapsActualChildBeforePropagatingCancellation()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("time.sleep(20)");
        using var stop = new CancellationTokenSource();
        var running = CliProcessRunner.RunAsync(child.StartInfo, TimeSpan.FromSeconds(30), stop.Token);
        try
        {
            await child.WaitForStartedAsync();
            stop.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(ControlledSolverProcess.GuardTimeout));
            child.ShouldHaveExited();
        }
        finally
        {
            stop.Cancel();
            await child.TerminateChildrenAsync();
            await Record.ExceptionAsync(() => running.WaitAsync(ControlledSolverProcess.GuardTimeout));
        }
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RootExit_RetiresDescendantAndDrainsInheritedPipesBeforeReturning(bool inheritOutput)
    {
        await using var child = await ControlledSolverProcess.CreateAsync($$"""
            import subprocess
            sys.stdin.read()
            descendant = subprocess.Popen(
                [sys.executable, "-u", "-c", "import time; time.sleep(20)"],
                stdin=subprocess.DEVNULL,
                stdout={{(inheritOutput ? "None" : "subprocess.DEVNULL")}},
                stderr={{(inheritOutput ? "None" : "subprocess.DEVNULL")}})
            with open("descendant.pid", "w") as pid_file:
                pid_file.write(str(descendant.pid))
            print("root completed", flush=True)
            os._exit(0)
            """);
        using var stop = new CancellationTokenSource();
        var running = CliProcessRunner.RunAsync(child.StartInfo, TimeSpan.FromSeconds(5), stop.Token);
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.ExitCode.ShouldBe(0);
            result.Output.ShouldBe("root completed");
            child.ShouldHaveExited();
            await child.DescendantsShouldHaveExitedAsync();
        }
        finally
        {
            stop.Cancel();
            await child.TerminateChildrenAsync();
            await Record.ExceptionAsync(() => running.WaitAsync(ControlledSolverProcess.GuardTimeout));
        }
    }

    [SkippableFact]
    public async Task LargeOutputAndInputEof_PreserveExitCodeAndBothStreams()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("""
            sys.stdout.write("o" * 1048576 + "\n")
            sys.stderr.write("e" * 1048576 + "\n")
            payload = sys.stdin.read()
            print("eof:" + str(len(payload)), flush=True)
            os._exit(7)
            """);
        var result = await CliProcessRunner.RunAsync(child.StartInfo, TimeSpan.FromSeconds(5), CancellationToken.None)
            .WaitAsync(ControlledSolverProcess.GuardTimeout);
        result.ExitCode.ShouldBe(7);
        result.Output.Count(character => character == 'o').ShouldBe(1048577); // payload + 'o' in "eof"
        result.Output.ShouldContain("eof:0");
        result.Output.ShouldEndWith(new string('e', 1048576));
        child.ShouldHaveExited();
    }

    [SkippableFact]
    public async Task Deadline_PreservesTimeoutResultAndReapsChild()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("time.sleep(20)");
        var result = await CliProcessRunner.RunAsync(child.StartInfo, TimeSpan.FromSeconds(2), CancellationToken.None)
            .WaitAsync(ControlledSolverProcess.GuardTimeout);
        result.ExitCode.ShouldBe(CliProcessRunner.TimedOutExitCode);
        result.Output.ShouldContain("Timed out after 2 s.");
        child.ShouldHaveExited();
    }
}
