using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Solvers;
using CAP.Avalonia.ViewModels.Solvers;
using CAP_Core.Solvers.Fdtd;
using Moq;
using Shouldly;

namespace UnitTests.Solvers.Lifetime;

public sealed class FdtdPickerRetirementTests
{
    [Fact]
    public async Task Retirement_CancelsAndDrainsAvailabilityWithoutPublishingLateHint()
    {
        var barrier = new RetirementBarrier();
        var backend = new Mock<IFdtdSMatrixService>();
        backend.Setup(service => service.CheckAvailabilityAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken token) => { await barrier.WaitAsync(token); return FdtdAvailability.Unavailable("late verdict"); });
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        using var picker = Create(backend.Object, path);
        var running = picker.CheckAvailabilityAsync();
        var stopping = Task.CompletedTask;
        try
        {
            await barrier.AwaitStartAsync(running);
            stopping = picker.StopAsync();
            await barrier.AssertDrainingAsync(stopping);
        }
        finally
        {
            await barrier.FinishAsync(running, stopping);
            File.Delete(path);
        }
        picker.AvailabilityHint.ShouldBeEmpty();
    }

    [Fact]
    public async Task Retirement_RejectsLaterAvailabilityProbe()
    {
        var backend = new Mock<IFdtdSMatrixService>();
        backend.Setup(service => service.CheckAvailabilityAsync(It.IsAny<CancellationToken>())).ReturnsAsync(FdtdAvailability.Available("ready"));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        using var picker = Create(backend.Object, path);
        try
        {
            await picker.StopAsync();
            try { await picker.CheckAvailabilityAsync(); } catch (OperationCanceledException) { }
            backend.Verify(service => service.CheckAvailabilityAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
        finally { File.Delete(path); }
    }

    private static FdtdBackendSelectionViewModel Create(IFdtdSMatrixService backend, string path) =>
        new(new FdtdBackendRegistry(new Dictionary<FdtdBackendType, IFdtdSMatrixService>
        { [FdtdBackendType.MeepDocker] = backend }, new UserPreferencesService(path)));
}
