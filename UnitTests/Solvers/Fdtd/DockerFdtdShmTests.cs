using CAP.Avalonia.Services.Solvers;
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
}
