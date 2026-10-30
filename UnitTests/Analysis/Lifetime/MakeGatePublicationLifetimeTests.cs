using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services.Localization;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class MakeGatePublicationLifetimeTests
{
    [AvaloniaTheory]
    [InlineData("edit", false)]
    [InlineData("replace", false)]
    [InlineData("remove", false)]
    [InlineData("selection", false)]
    [InlineData("close", false)]
    [InlineData("roles", false)]
    [InlineData("edit", true)]
    [InlineData("selection", true)]
    public async Task ObsoleteExtraction_CannotPublishRowsOrLateFailure(string change, bool fail)
    {
        await using var fixture = new MakeGateLifetimeFixture();
        fixture.Extractor.FailOnRelease = fail;
        var running = fixture.Panel.SimulateCommand.ExecuteAsync(null);
        string statusAfterChange = "";
        try
        {
            await fixture.Extractor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.ChangeContext(change);
            statusAfterChange = fixture.Panel.StatusText;
        }
        finally { await fixture.FinishAsync(running); }

        fixture.Panel.HasResult.ShouldBeFalse();
        fixture.Panel.Rows.ShouldBeEmpty();
        fixture.Panel.StatusText.ShouldBe(statusAfterChange, "the old solve cannot replace the current editor's status");
        fixture.Panel.IsSaved.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Stop_RevokesPublicationAndAwaitsActualWorkerDelivery()
    {
        await using var fixture = new MakeGateLifetimeFixture();
        var running = fixture.Panel.SimulateCommand.ExecuteAsync(null);
        Task? stopping = null;
        try
        {
            await fixture.Extractor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stopping = fixture.Panel.StopAsync();
            stopping.IsCompleted.ShouldBeFalse("retirement must drain the accepted extraction");
            await fixture.Extractor.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { await fixture.FinishAsync(running); }

        await stopping!.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Panel.HasResult.ShouldBeFalse();
        fixture.Panel.Rows.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task OrdinaryCancel_KeepsItsFeedbackWithoutPublishingRows()
    {
        await using var fixture = new MakeGateLifetimeFixture();
        fixture.Extractor.HonorCancellation = true;
        var running = fixture.Panel.SimulateCommand.ExecuteAsync(null);
        try
        {
            await fixture.Extractor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Panel.CancelSimulationCommand.Execute(null);
        }
        finally { await fixture.FinishAsync(running); }

        fixture.Panel.HasResult.ShouldBeFalse();
        fixture.Panel.StatusText.ShouldBe(LocalizationService.Instance.Translate("MakeGate.Cancelled"));
        fixture.Panel.IsProcessing.ShouldBeFalse();
    }
}
