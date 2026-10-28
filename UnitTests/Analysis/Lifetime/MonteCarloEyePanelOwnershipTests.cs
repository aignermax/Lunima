using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Analysis.MonteCarloAnalysis;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class MonteCarloEyePanelOwnershipTests
{
    [AvaloniaTheory]
    [InlineData("edit", false)]
    [InlineData("replace", false)]
    [InlineData("configure", false)]
    [InlineData("configure", true)]
    public async Task EyeWorker_RejectsObsoleteResultsAndFailures(string change, bool failure)
    {
        await using var fixture = new MonteCarloLifetimeFixture(eye: true);
        fixture.Runner.Fail = failure;
        var plot = fixture.Panel.PlotModel;
        var running = fixture.RunAsync();
        var status = "";
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            fixture.Change(change);
            status = fixture.Panel.StatusText;
        }
        finally { await fixture.Runner.Barrier.FinishAsync(running); }

        fixture.Panel.PlotModel.ShouldBeSameAs(plot);
        fixture.Panel.HasResult.ShouldBeFalse();
        fixture.Panel.StatusText.ShouldBe(status);
        fixture.Errors.Entries.ShouldBeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EyeRetirement_DrainsTypedWorkerAndRejectsQueuedProgress(bool applicationOwned)
    {
        var application = new ApplicationWorkCoordinator();
        await using var fixture = new MonteCarloLifetimeFixture(application, eye: true);
        var running = fixture.RunAsync();
        Task stopping = Task.CompletedTask;
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            stopping = applicationOwned ? application.StopAsync() : fixture.Panel.StopAsync();
            await fixture.Runner.Barrier.AssertDrainingAsync(stopping);
            var status = fixture.Panel.StatusText;
            var progress = fixture.Panel.ProgressPercent;
            fixture.Runner.Progress!.Report(new(2, 3));
            Dispatcher.UIThread.RunJobs();
            fixture.Panel.StatusText.ShouldBe(status);
            fixture.Panel.ProgressPercent.ShouldBe(progress);
        }
        finally { await fixture.Runner.Barrier.FinishAsync(running, stopping); }

        fixture.Panel.HasResult.ShouldBeFalse();
        fixture.Panel.IsRunning.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task OrdinaryEyeCancel_KeepsTheExistingFeedback()
    {
        await using var fixture = new MonteCarloLifetimeFixture(eye: true);
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
    }

    [AvaloniaFact]
    public async Task CompletedEye_HasCapturedSettingsAndAnOwnedHistogram()
    {
        await using var fixture = new MonteCarloLifetimeFixture(eye: true);
        var running = fixture.RunAsync();
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            fixture.Panel.WidthSigmaNm = 987;
        }
        finally { await fixture.Runner.Barrier.FinishAsync(running); }

        fixture.Panel.HasResult.ShouldBeTrue();
        fixture.Panel.SummaryText.ShouldContain("Δw 10.0 nm / Δt 5.0 nm");
        fixture.Panel.PlotModel.Series.ShouldNotBeEmpty();
        fixture.Errors.Entries.ShouldBeEmpty();
    }
}
