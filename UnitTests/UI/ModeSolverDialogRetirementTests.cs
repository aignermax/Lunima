using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Solvers;
using CAP.Avalonia.Views.Dialogs;
using CAP_Core.Solvers.ModeSolver;
using Moq;
using Shouldly;
using UnitTests.Solvers.Lifetime;

namespace UnitTests.UI;

public sealed class ModeSolverDialogRetirementTests
{
    [AvaloniaFact]
    public async Task Closing_CancelsSolveAndKeepsWindowAliveUntilActualCleanupFinishes()
    {
        var barrier = new RetirementBarrier();
        var service = new Mock<IModeSolverService>();
        service.Setup(s => s.SolveAsync(It.IsAny<ModeSolverRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (ModeSolverRequest _, CancellationToken token) =>
            {
                await barrier.WaitAsync(token);
                return new ModeSolverResult { Success = true };
            });
        await using var model = new ModeSolverViewModel(service.Object);
        var window = new ModeSolverDialog { DataContext = model };
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        var solving = model.SolveCommand.ExecuteAsync(null);
        try
        {
            await barrier.AwaitStartAsync(solving);
            window.Close();
            window.IsVisible.ShouldBeTrue("the worker still owns the window's feature scope");
            window.IsEnabled.ShouldBeFalse();
            await barrier.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            closed.Task.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            await barrier.FinishAsync(solving);
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        model.HasResult.ShouldBeFalse();
        model.CanSolve.ShouldBeFalse();
    }
}
