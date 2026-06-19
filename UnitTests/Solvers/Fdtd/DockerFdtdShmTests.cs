using System;
using System.Threading;
using System.Threading.Tasks;
using CAP.Avalonia.Services.Solvers;
using CAP_Core.Solvers.Fdtd;
using Shouldly;
using Xunit;

namespace UnitTests.Solvers.Fdtd;

/// <summary>
/// Verifies the dynamic /dev/shm sizing that scales with the MPI rank count
/// (which itself scales with the host's cores), clamped to a floor and ceiling.
/// </summary>
public class DockerFdtdShmTests
{
    [Theory]
    [InlineData(1, 2048)]    // tiny machine → floor
    [InlineData(4, 2048)]    // 4×256=1024 → still floor
    [InlineData(16, 4096)]   // 16×256 → scales
    [InlineData(24, 6144)]   // 24×256 → scales
    [InlineData(128, 16384)] // huge machine → ceiling
    public void ResolveShmMb_ScalesWithCores_WithinFloorAndCeiling(int cores, int expectedMb)
    {
        DockerFdtdSMatrixService.ResolveShmMb(cores).ShouldBe(expectedMb);
    }

    [Fact]
    public void CleanProgressLine_StripsTqdmBarArt()
    {
        var raw = " 50%|███▉     | 3/4 [00:03<00:01, 1.2it/s]";

        var clean = DockerFdtdSMatrixService.CleanProgressLine(raw);

        clean.ShouldBe("50% 3/4 [00:03<00:01, 1.2it/s]");
        clean.ShouldNotContain("█");
    }

    [Fact]
    public void CleanProgressLine_DropsTrailingWarningConcatenatedOntoTheBar()
    {
        // tqdm writes the bar with '\r', so a following warning lands on the same line.
        var raw = " 0%|          | 0/2 [00:00<?,?it/s]/opt/conda/envs/mp/lib/python3.13/site-packages/meep/__init__.py:4437: ComplexWarning: Casting complex values";

        var clean = DockerFdtdSMatrixService.CleanProgressLine(raw);

        clean.ShouldBe("0% 0/2 [00:00<?,?it/s]");
        clean.ShouldNotContain("ComplexWarning");
        clean.ShouldNotContain("/opt/");
    }

    [Fact]
    public async Task SolveAsync_SecondRun_WaitsWhileAnotherSolveHoldsTheGate()
    {
        // Simulate a solve already in flight by holding the process-wide gate.
        await DockerFdtdSMatrixService.SolveGate.WaitAsync();
        try
        {
            var svc = new DockerFdtdSMatrixService("img:1", "Dockerfile", "ctx");
            var req = new FdtdSMatrixRequest { Polygons = new[] { new FdtdPolygon() } };
            using var cts = new CancellationTokenSource();

            var solve = svc.SolveAsync(req, ct: cts.Token);

            // Gate held → the solve must block before it ever touches Docker.
            var firstDone = await Task.WhenAny(solve, Task.Delay(400));
            firstDone.ShouldNotBe(solve, "a second solve must queue while the gate is held");

            // Cancelling the queued wait surfaces as a normal cancellation (not a hang).
            cts.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => solve);
        }
        finally
        {
            DockerFdtdSMatrixService.SolveGate.Release();
        }
    }
}
