using Avalonia.Headless.XUnit;
using CAP_Core.Solvers.Fdtd;
using Moq;
using Shouldly;

namespace UnitTests.Solvers.Lifetime;

public sealed class ComponentEditorRetirementTests
{
    [AvaloniaTheory]
    [InlineData("preview", false)]
    [InlineData("solve", false)]
    [InlineData("estimate", true)]
    [InlineData("solve", true)]
    public async Task Retirement_DrainsPreviewLocalAndCloudJobsWithoutLateResults(string stage, bool cloud)
    {
        var fixture = new ComponentJobFixture(stage, cloud);
        using var model = fixture.NewEditor();
        var running = fixture.StartAsync(model);
        var stopping = Task.CompletedTask;
        try
        {
            await fixture.Barrier.AwaitStartAsync(running);
            stopping = model.StopAsync();
            await fixture.Barrier.AssertDrainingAsync(stopping);
        }
        finally { await fixture.Barrier.FinishAsync(running, stopping); }
        if (stage == "preview") model.HasPreview.ShouldBeFalse();
        model.HasSMatrix.ShouldBeFalse();
        model.IsAwaitingCloudConfirmation.ShouldBeFalse();
        model.IsBusy.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Retirement_RejectsLaterPreview()
    {
        var fixture = new ComponentJobFixture("preview");
        fixture.Barrier.Release.TrySetResult();
        using var model = fixture.NewEditor();
        await model.StopAsync();
        await model.RunPreviewCommand.ExecuteAsync(null);
        fixture.Renderer.Verify(renderer => renderer.RenderRawCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [AvaloniaFact]
    public async Task Retirement_DiscardsPendingCloudConfirmationWithoutSubmitting()
    {
        var fixture = new ComponentJobFixture("solve", cloud: true);
        fixture.Barrier.Release.TrySetResult();
        using var model = fixture.NewEditor();
        await model.ComputeSMatrixCommand.ExecuteAsync(null);
        model.IsAwaitingCloudConfirmation.ShouldBeTrue();
        await model.StopAsync();
        await model.ConfirmCloudSubmitCommand.ExecuteAsync(null);
        fixture.Solver.Verify(service => service.SolveAsync(It.IsAny<FdtdSMatrixRequest>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
        model.IsAwaitingCloudConfirmation.ShouldBeFalse();
    }
}
