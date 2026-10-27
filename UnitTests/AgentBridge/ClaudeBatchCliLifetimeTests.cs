using CAP.Avalonia.Services.AgentBridge.Registration;
using Shouldly;
using UnitTests.Solvers;

namespace UnitTests.AgentBridge;

public sealed class ClaudeBatchCliLifetimeTests
{
    [SkippableFact]
    public async Task CallerCancellation_ReapsShellAndHeldWorkerBeforeReturning()
    {
        await using var fixture = await ClaudeBatchCliFixture.CreateAsync("time.sleep(20)");
        using var stop = new CancellationTokenSource();
        var running = CliProcessRunner.RunAsync(fixture.Build(["mcp", "get", "lunima"]), TimeSpan.FromSeconds(30), stop.Token);
        try
        {
            await fixture.WaitForStartedAsync(running);
            stop.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(ControlledSolverProcess.GuardTimeout));
            fixture.Child.ShouldHaveExited();
            await fixture.Child.DescendantsShouldHaveExitedAsync();
        }
        finally { await fixture.CleanupAsync(stop, running); }
    }

    [SkippableFact]
    public async Task Deadline_ReapsShellAndHeldWorkerBeforeReportingTimeout()
    {
        await using var fixture = await ClaudeBatchCliFixture.CreateAsync("time.sleep(20)");
        using var stop = new CancellationTokenSource();
        var running = CliProcessRunner.RunAsync(fixture.Build([]), TimeSpan.FromSeconds(2), stop.Token);
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.ExitCode.ShouldBe(CliProcessRunner.TimedOutExitCode, result.Output);
            result.Output.ShouldContain("Timed out after 2 s.");
            fixture.Child.ShouldHaveExited();
            await fixture.Child.DescendantsShouldHaveExitedAsync();
        }
        finally { await fixture.CleanupAsync(stop, running); }
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedShim_RetiresDescendantWithInheritedOrRedirectedPipes(bool inheritOutput)
    {
        await using var fixture = await ClaudeBatchCliFixture.CreateAsync($$"""
            import subprocess
            descendant = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(20)"],
                stdin=subprocess.DEVNULL,
                stdout={{(inheritOutput ? "None" : "subprocess.DEVNULL")}},
                stderr={{(inheritOutput ? "None" : "subprocess.DEVNULL")}})
            with open("descendant.pid", "w") as marker:
                marker.write(str(descendant.pid))
            print("completed", flush=True)
            os._exit(0)
            """);
        using var stop = new CancellationTokenSource();
        var running = CliProcessRunner.RunAsync(fixture.Build([]), TimeSpan.FromSeconds(5), stop.Token);
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.ExitCode.ShouldBe(0, result.Output);
            result.Output.ShouldBe("completed");
            fixture.Child.ShouldHaveExited();
            await fixture.Child.DescendantsShouldHaveExitedAsync();
        }
        finally { await fixture.CleanupAsync(stop, running); }
    }
}
