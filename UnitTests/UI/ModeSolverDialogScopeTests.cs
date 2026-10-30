using Avalonia.Headless.XUnit;
using CAP.Avalonia.DI;
using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.ViewModels.Solvers;
using CAP_Core.Solvers.ModeSolver;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;
using UnitTests.Solvers.Lifetime;

namespace UnitTests.UI;

public sealed class ModeSolverDialogScopeTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WindowOrApplicationRetirement_DrainsWorkerAndDisposesChildScope(bool applicationClose)
    {
        var application = new ApplicationWorkCoordinator();
        var worker = new RetirementBarrier();
        var scopeDisposal = new ScopeDisposal();
        var service = new Mock<IModeSolverService>();
        service.Setup(s => s.SolveAsync(It.IsAny<ModeSolverRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (ModeSolverRequest _, CancellationToken token) =>
            { await worker.WaitAsync(token); return new ModeSolverResult { Success = true }; });
        var services = new ServiceCollection();
        services.AddSingleton(application);
        services.AddTransient<ScopeDisposal>(_ => scopeDisposal);
        services.AddTransient(scopeServices =>
        {
            scopeServices.GetRequiredService<ScopeDisposal>();
            return new ModeSolverViewModel(service.Object, applicationWork: application);
        });
        await using var provider = services.BuildServiceProvider();
        var window = (await ModeSolverWindowComposition.CreateAsync(provider))!;
        var model = (ModeSolverViewModel)window.DataContext!;
        var closed = Signal();
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        var running = model.SolveCommand.ExecuteAsync(null);
        var stopping = Task.CompletedTask;
        try
        {
            await worker.AwaitStartAsync(running);
            if (applicationClose) stopping = application.StopAsync();
            else window.Close();
            window.IsEnabled.ShouldBeFalse();
            await worker.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            scopeDisposal.Started.Task.IsCompleted.ShouldBeFalse();
            worker.Release.TrySetResult();
            await scopeDisposal.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            window.IsVisible.ShouldBeTrue("asynchronous scoped resource cleanup still owns this dialog");
            if (applicationClose) stopping.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            worker.Release.TrySetResult();
            scopeDisposal.Release.TrySetResult();
            await Task.WhenAll(running, stopping).WaitAsync(TimeSpan.FromSeconds(5));
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        scopeDisposal.Calls.ShouldBe(1, "the dialog owns a child scope and releases it while the root provider remains alive");
        model.HasResult.ShouldBeFalse();
    }

    private sealed class ScopeDisposal : IAsyncDisposable
    {
        public TaskCompletionSource Started { get; } = Signal();
        public TaskCompletionSource Release { get; } = Signal();
        public int Calls;
        public async ValueTask DisposeAsync()
        {
            Calls++;
            Started.TrySetResult();
            await Release.Task;
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
