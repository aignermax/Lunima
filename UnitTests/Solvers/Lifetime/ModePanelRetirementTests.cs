using CAP.Avalonia.ViewModels.Solvers;
using CAP.Avalonia.ViewModels.Solvers.ModeProbe;
using CAP_Core.Solvers.ModeProbe;
using CAP_Core.Solvers.ModeSolver;
using Moq;
using Shouldly;

namespace UnitTests.Solvers.Lifetime;

public sealed class ModePanelRetirementTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Retirement_CancelsAndDrainsSolveOrInstallWithoutPublishing(bool probe, bool installing)
    {
        var barrier = new RetirementBarrier();
        var service = new Mock<IModeSolverService>();
        var calls = 0;
        service.Setup(s => s.SolveAsync(It.IsAny<ModeSolverRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (ModeSolverRequest _, CancellationToken token) =>
            {
                calls++;
                if (installing) return new ModeSolverResult { Success = false, MissingBackend = "test-backend" };
                await barrier.WaitAsync(token);
                return Success();
            });
        var manual = new ModeSolverViewModel(service.Object);
        var flyout = new ModeProbeViewModel(service.Object, new CrossSectionDefaultsStore());
        async Task<bool> Install(string package, IProgress<string> progress, CancellationToken token)
        { await barrier.WaitAsync(token); return true; }
        manual.EnsureBackendAsync = Install;
        flyout.EnsureBackendAsync = Install;
        Task running;
        if (probe)
        {
            flyout.Open(ProbeTarget.ForConnection(0.5, 12), 10, 20);
            running = flyout.SolveCommand.ExecutionTask!;
        }
        else running = manual.SolveCommand.ExecuteAsync(null);
        var stopping = Task.CompletedTask;
        try
        {
            await barrier.AwaitStartAsync(running);
            stopping = probe ? flyout.StopAsync() : manual.StopAsync();
            await barrier.AssertDrainingAsync(stopping);
        }
        finally { await barrier.FinishAsync(running, stopping); }

        (probe ? flyout.HasResult : manual.HasResult).ShouldBeFalse();
        calls.ShouldBe(1, "retired installation must not start a retry solve");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retirement_RejectsLaterSolves(bool probe)
    {
        var service = new Mock<IModeSolverService>();
        service.Setup(s => s.SolveAsync(It.IsAny<ModeSolverRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Success());
        if (probe)
        {
            var model = new ModeProbeViewModel(service.Object, new CrossSectionDefaultsStore());
            await model.StopAsync();
            model.Open(ProbeTarget.ForConnection(0.5, 12), 0, 0);
        }
        else
        {
            var model = new ModeSolverViewModel(service.Object);
            await model.StopAsync();
            await model.SolveCommand.ExecuteAsync(null);
        }
        service.Verify(s => s.SolveAsync(It.IsAny<ModeSolverRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ModeSolverResult Success() => new()
    {
        Success = true, BackendUsed = "test",
        Modes = new[] { new ModeSolverModeEntry { ModeIndex = 0, NEff = 2.4, NGroup = 4.2, Wavelength = 1.55 } },
    };
}
