using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP_Core.Solvers.Fdtd;
using Moq;
using Shouldly;

namespace UnitTests.Solvers.Lifetime;

public sealed class ComponentEditorPublicationTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retirement_DoesNotPublishLateCancellationStatus(bool cloud)
    {
        var fixture = new ComponentJobFixture("solve", cloud);
        var model = fixture.NewEditor();
        var running = fixture.StartAsync(model);
        var stopping = Task.CompletedTask;
        var status = string.Empty;
        try
        {
            await fixture.Barrier.AwaitStartAsync(running);
            status = model.StatusText;
            stopping = model.StopAsync();
        }
        finally { await fixture.Barrier.FinishAsync(running, stopping); }
        model.StatusText.ShouldBe(status);
    }

    [AvaloniaFact]
    public async Task CompletedSolve_IgnoresQueuedProgressFromItsOldWorker()
    {
        var fixture = new ComponentJobFixture("unused");
        IProgress<string>? captured = null;
        fixture.Solver.Setup(service => service.SolveAsync(It.IsAny<FdtdSMatrixRequest>(),
                It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .Returns((FdtdSMatrixRequest _, IProgress<string>? progress, CancellationToken _) =>
            {
                captured = progress;
                return Task.FromResult(new FdtdSMatrixResult { Success = false, Error = "finished" });
            });
        var model = fixture.NewEditor();
        try
        {
            await model.ComputeSMatrixCommand.ExecuteAsync(null);
            var status = model.StatusText;
            captured.ShouldNotBeNull();
            captured.Report("late progress from the completed operation");
            Dispatcher.UIThread.RunJobs();
            model.StatusText.ShouldBe(status);
        }
        finally { await model.StopAsync(); }
    }
}
