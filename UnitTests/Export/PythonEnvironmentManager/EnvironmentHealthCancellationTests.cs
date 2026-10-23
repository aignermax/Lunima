using CAP_Core.Export;
using CAP_Core.Export.PythonEnvironmentManager;
using Shouldly;
using UnitTests.Solvers.Lifetime;

namespace UnitTests.Export.PythonEnvironmentManager;

public sealed class EnvironmentHealthCancellationTests
{
    [Fact]
    public async Task HealthProbe_PropagatesCancellationThroughInterpreterDiscovery()
    {
        var environment = new PythonEnvironment { Name = "probe", VenvPath = Path.Combine(Path.GetTempPath(), "health-cancel-" + Guid.NewGuid()) };
        Directory.CreateDirectory(Path.GetDirectoryName(environment.PythonExecutable)!);
        await File.WriteAllTextAsync(environment.PythonExecutable, "probe boundary only");
        var barrier = new RetirementBarrier();
        using var stop = new CancellationTokenSource();
        var pending = new EnvironmentHealthChecker(new Discovery(barrier)).CheckAsync(environment, stop.Token);
        try
        {
            await barrier.AwaitStartAsync(pending);
            stop.Cancel();
            await Task.WhenAny(barrier.Cancelled.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            barrier.Cancelled.Task.IsCompleted.ShouldBeTrue();
            pending.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            await barrier.FinishAsync(pending);
            Directory.Delete(environment.VenvPath, true);
        }
        pending.IsCanceled.ShouldBeTrue("a cancelled health probe must not publish a broken-environment verdict");
    }

    private sealed class Discovery(RetirementBarrier barrier) : PythonDiscoveryService
    {
        public override async Task<PythonInstallation?> CheckPythonInstallation(string pythonPath, string source,
            bool probeGdsFactory = false, CancellationToken cancellationToken = default)
        { await barrier.WaitAsync(cancellationToken); return null; }
    }
}
