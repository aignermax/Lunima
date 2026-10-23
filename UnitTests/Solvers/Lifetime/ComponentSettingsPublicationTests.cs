using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services.Notifications;
using CAP_Core;
using CAP_Core.Documents;
using CAP_Core.Solvers.Fdtd;
using CAP_DataAccess.Persistence.PIR;
using Moq;
using Shouldly;

namespace UnitTests.Solvers.Lifetime;

public sealed class ComponentSettingsPublicationTests
{
    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task ObsoleteSolve_DoesNotPublishStatusNotificationsOrErrors(bool cloud, bool documentChanged, bool fails)
    {
        var fixture = new ComponentJobFixture("solve", cloud);
        if (fails)
            fixture.Solver.Setup(service => service.SolveAsync(It.IsAny<FdtdSMatrixRequest>(),
                    It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
                .Returns(async (FdtdSMatrixRequest _, IProgress<string>? _, CancellationToken ct) =>
                {
                    await fixture.Barrier.WaitAsync(ct);
                    throw new InvalidOperationException("late backend failure");
                });
        var notifications = new Mock<INotificationService>();
        var errors = new ErrorConsoleService();
        var document = new DocumentSession();
        var model = fixture.Settings(document, notifications.Object, errors);
        var running = fixture.StartAsync(model);
        var stopping = Task.CompletedTask;
        var status = string.Empty;
        try
        {
            await fixture.Barrier.AwaitStartAsync(running);
            status = model.SolverStatus;
            if (documentChanged) document.Invalidate();
            else stopping = model.StopAsync();
        }
        finally { await fixture.Barrier.FinishAsync(running, stopping); }
        try
        {
            model.SolverStatus.ShouldBe(status, "obsolete completion must not overwrite the visible state");
            notifications.Invocations.ShouldBeEmpty();
            errors.Entries.ShouldBeEmpty();
        }
        finally
        {
            await model.StopAsync();
            await document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UserCancellation_InCurrentDocumentPreservesCancellationFeedback(bool cloud)
    {
        var fixture = new ComponentJobFixture("solve", cloud);
        var notifications = new Mock<INotificationService>();
        var document = new DocumentSession();
        var model = fixture.Settings(document, notifications.Object);
        var running = fixture.StartAsync(model);
        try
        {
            await fixture.Barrier.AwaitStartAsync(running);
            model.CancelRecalculate();
        }
        finally { await fixture.Barrier.FinishAsync(running); }
        try { notifications.Verify(service => service.ShowInfo(It.IsAny<string>(), null), Times.Once); }
        finally
        {
            await model.StopAsync();
            await document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Configure_RejectsTargetReplacementWhileSolveOwnsOriginalTarget(bool cloud)
    {
        var fixture = new ComponentJobFixture("solve", cloud);
        var document = new DocumentSession();
        var model = fixture.Settings(document);
        var replacement = new Dictionary<string, ComponentSMatrixData>();
        var running = fixture.StartAsync(model);
        try
        {
            await fixture.Barrier.AwaitStartAsync(running);
            Should.Throw<InvalidOperationException>(() => model.Configure("other", "other", "Other", replacement,
                liveComponent: TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins(), document: document));
        }
        finally
        {
            await fixture.Barrier.FinishAsync(running);
            await model.StopAsync();
            await document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        replacement.ShouldBeEmpty();
        fixture.Store.ShouldContainKey("component");
    }
}
