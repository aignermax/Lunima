using Avalonia.Headless.XUnit;
using CAP_Core.Documents;
using CAP_Core.Solvers.Fdtd;
using Moq;
using Shouldly;

namespace UnitTests.Solvers.Lifetime;

public sealed class ComponentSettingsRetirementTests
{
    [AvaloniaTheory]
    [InlineData("availability", false)]
    [InlineData("geometry", false)]
    [InlineData("solve", false)]
    [InlineData("estimate", true)]
    [InlineData("solve", true)]
    public async Task Retirement_DrainsDialogJobsWithoutMutatingLiveOrTemplateState(string stage, bool cloud)
    {
        var fixture = new ComponentJobFixture(stage, cloud);
        var model = fixture.Settings();
        var running = fixture.StartAsync(model);
        var stopping = Task.CompletedTask;
        var changesBeforeRetirement = 0;
        try
        {
            await fixture.Barrier.AwaitStartAsync(running);
            changesBeforeRetirement = fixture.Changes;
            stopping = model.StopAsync();
            await fixture.Barrier.AssertDrainingAsync(stopping);
        }
        finally { await fixture.Barrier.FinishAsync(running, stopping); }
        fixture.Store.ShouldBeEmpty();
        fixture.Propagations.ShouldBe(0);
        fixture.Changes.ShouldBe(changesBeforeRetirement);
        model.IsAwaitingCloudConfirmation.ShouldBeFalse();
        model.IsComputing.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Retirement_RejectsLaterRecalculation()
    {
        var fixture = new ComponentJobFixture("solve");
        fixture.Barrier.Release.TrySetResult();
        var model = fixture.Settings();
        await model.StopAsync();
        await model.RecalculateSMatrixCommand.ExecuteAsync(null);
        fixture.Solver.Verify(service => service.CheckAvailabilityAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [AvaloniaFact]
    public async Task Retirement_DiscardsPendingCloudConfirmationWithoutSubmitting()
    {
        var fixture = new ComponentJobFixture("solve", cloud: true);
        fixture.Barrier.Release.TrySetResult();
        var model = fixture.Settings();
        await model.RecalculateSMatrixCommand.ExecuteAsync(null);
        model.IsAwaitingCloudConfirmation.ShouldBeTrue();
        await model.StopAsync();
        await model.ConfirmCloudSubmitCommand.ExecuteAsync(null);
        fixture.Solver.Verify(service => service.SolveAsync(It.IsAny<FdtdSMatrixRequest>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
        model.IsAwaitingCloudConfirmation.ShouldBeFalse();
    }

    [AvaloniaTheory]
    [InlineData("edit")]
    [InlineData("scope")]
    [InlineData("replace")]
    public async Task ChangedDocument_RejectsOldSolveBeforeAnyLiveOrTemplateCommit(string change)
    {
        var fixture = new ComponentJobFixture("solve");
        var document = new DocumentSession();
        var model = fixture.Settings(document);
        var running = fixture.StartAsync(model);
        try
        {
            await fixture.Barrier.AwaitStartAsync(running);
            if (change == "edit") document.Invalidate();
            else if (change == "scope") document.ChangeScope(Guid.NewGuid());
            else document.ReplaceDocument();
        }
        finally { await fixture.Barrier.FinishAsync(running); }
        fixture.Store.ShouldBeEmpty();
        fixture.Propagations.ShouldBe(0);
        fixture.Changes.ShouldBe(0);
        await model.StopAsync();
        await document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }
}
