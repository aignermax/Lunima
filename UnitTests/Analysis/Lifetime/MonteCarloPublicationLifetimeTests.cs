using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Analysis.MonteCarloAnalysis;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class MonteCarloPublicationLifetimeTests
{
    [AvaloniaTheory]
    [InlineData("edit", false)]
    [InlineData("replace", false)]
    [InlineData("configure", false)]
    [InlineData("edit", true)]
    [InlineData("configure", true)]
    public async Task SupersededSample_CannotPublishResultsOrFailures(string change, bool fail)
    {
        await using var fixture = new MonteCarloLifetimeFixture();
        fixture.Runner.Fail = fail;
        var originalPlot = fixture.Panel.PlotModel;
        var running = fixture.RunAsync();
        var status = "";
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            fixture.Change(change);
            status = fixture.Panel.StatusText;
        }
        finally { await fixture.Runner.Barrier.FinishAsync(running); }

        fixture.Panel.PlotModel.ShouldBeSameAs(originalPlot);
        fixture.Panel.HasResult.ShouldBeFalse();
        fixture.Panel.StatusText.ShouldBe(status);
        fixture.Errors.Entries.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task QueuedProgress_AfterRetirementCannotChangeStatusOrProgress()
    {
        await using var fixture = new MonteCarloLifetimeFixture();
        var running = fixture.RunAsync();
        Task stopping = Task.CompletedTask;
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            stopping = fixture.Panel.StopAsync();
            var status = fixture.Panel.StatusText;
            var progress = fixture.Panel.ProgressPercent;
            fixture.Runner.Progress!.Report(new MonteCarloProgress(2, 3));
            Dispatcher.UIThread.RunJobs();
            fixture.Panel.StatusText.ShouldBe(status);
            fixture.Panel.ProgressPercent.ShouldBe(progress);
        }
        finally { await fixture.Runner.Barrier.FinishAsync(running, stopping); }
    }

    [AvaloniaFact]
    public async Task SettingsChangedDuringRun_SummaryDescribesTheCapturedFabricationInputs()
    {
        await using var fixture = new MonteCarloLifetimeFixture();
        var running = fixture.RunAsync();
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            fixture.Panel.WidthSigmaNm = 987;
            fixture.Panel.ThicknessSigmaNm = 654;
        }
        finally { await fixture.Runner.Barrier.FinishAsync(running); }

        fixture.Panel.HasResult.ShouldBeTrue();
        fixture.Panel.SummaryText.ShouldContain("Δw 10.0 nm / Δt 5.0 nm");
    }

    [AvaloniaFact]
    public async Task PlotObserver_ReconfigureRevokesTheRemainingSummaryAndStatus()
    {
        await using var fixture = new MonteCarloLifetimeFixture();
        fixture.Runner.Barrier.Release.TrySetResult();
        var revoked = false;
        string? summary = null, status = null;
        fixture.Panel.PropertyChanged += (_, args) =>
        {
            if (revoked || args.PropertyName != nameof(fixture.Panel.PlotModel)) return;
            revoked = true;
            fixture.Panel.Configure(fixture.OtherCanvas);
            summary = fixture.Panel.SummaryText;
            status = fixture.Panel.StatusText;
        };

        await fixture.RunAsync();

        revoked.ShouldBeTrue();
        fixture.Panel.SummaryText.ShouldBe(summary);
        fixture.Panel.StatusText.ShouldBe(status);
    }

    [AvaloniaFact]
    public async Task OrdinaryUserCancel_KeepsCancellationFeedback()
    {
        await using var fixture = new MonteCarloLifetimeFixture();
        fixture.Runner.HonorCancellation = true;
        var running = fixture.RunAsync();
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            fixture.Panel.CancelAnalysisCommand.Execute(null);
        }
        finally { await fixture.Runner.Barrier.FinishAsync(running); }

        fixture.Panel.StatusText.ShouldBe(LocalizationService.Instance.Translate("Analysis.Mc.Cancelled"));
        fixture.Panel.HasResult.ShouldBeFalse();
        fixture.Panel.IsRunning.ShouldBeFalse();
    }
}
