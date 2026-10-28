using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.ViewModels.Solvers;
using CAP.Avalonia.ViewModels.Solvers.ModeProbe;
using CAP_Core.Solvers.ModeProbe;
using CAP_Core.Solvers.ModeSolver;
using Moq;
using Shouldly;

namespace UnitTests.Solvers.Lifetime;

public sealed class FeatureApplicationRetirementTests
{
    [Fact]
    public async Task ApplicationRetirement_CancelsAndDrainsRegisteredManualSolver()
    {
        var application = new ApplicationWorkCoordinator();
        var barrier = new RetirementBarrier();
        var service = new Mock<IModeSolverService>();
        service.Setup(s => s.SolveAsync(It.IsAny<ModeSolverRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (ModeSolverRequest _, CancellationToken ct) =>
            { await barrier.WaitAsync(ct); return new ModeSolverResult { Success = true }; });
        await using var model = new ModeSolverViewModel(service.Object, applicationWork: application);
        var running = model.SolveCommand.ExecuteAsync(null);
        var stopping = Task.CompletedTask;
        try
        {
            await barrier.AwaitStartAsync(running);
            stopping = application.StopAsync();
            await barrier.AssertDrainingAsync(stopping);
        }
        finally { await barrier.FinishAsync(running, stopping); }
        model.HasResult.ShouldBeFalse();
    }

    [Fact]
    public async Task RetiredApplication_RefusesLazySolverAndProbeConstruction()
    {
        var application = new ApplicationWorkCoordinator();
        await application.StopAsync();
        var service = Mock.Of<IModeSolverService>();
        Should.Throw<InvalidOperationException>(() => new ModeSolverViewModel(service, applicationWork: application));
        Should.Throw<InvalidOperationException>(() => new ModeProbeViewModel(service, new CrossSectionDefaultsStore(), application));
    }
}
