using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services.Lifetime;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.Analysis.Lifetime;

public sealed class MonteCarloRetirementTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retirement_CancelsTheWorkerAndWaitsForActualCleanup(bool applicationOwned)
    {
        var application = new ApplicationWorkCoordinator();
        await using var fixture = new MonteCarloLifetimeFixture(application);
        var running = fixture.RunAsync();
        Task stopping = Task.CompletedTask;
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            stopping = applicationOwned ? application.StopAsync() : fixture.Panel.StopAsync();
            await fixture.Runner.Barrier.AssertDrainingAsync(stopping);
        }
        finally { await fixture.Runner.Barrier.FinishAsync(running, stopping); }

        fixture.Panel.HasResult.ShouldBeFalse();
        fixture.Panel.IsRunning.ShouldBeFalse();
    }

    [AvaloniaTheory]
    [InlineData("stop")]
    [InlineData("replace")]
    public async Task ObsoleteConfiguredPanel_RejectsNewWork(string change)
    {
        await using var fixture = new MonteCarloLifetimeFixture();
        fixture.Runner.Barrier.Release.TrySetResult();
        if (change == "stop") await fixture.Panel.StopAsync();
        else fixture.Canvas.Document.ReplaceDocument();

        await fixture.RunAsync();

        fixture.Runner.Calls.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task MainRetirement_DrainsItsActualMonteCarloPanel()
    {
        await using var fixture = new MonteCarloLifetimeFixture();
        await using var shell = MainViewModelTestHelper.CreateMainViewModel(canvas: fixture.Canvas,
            monteCarloViewModel: fixture.Panel);
        var running = fixture.RunAsync();
        Task stopping = Task.CompletedTask;
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            stopping = shell.StopAsync();
            await fixture.Runner.Barrier.AssertDrainingAsync(stopping);
        }
        finally { await fixture.Runner.Barrier.FinishAsync(running, stopping); }

        fixture.Panel.HasResult.ShouldBeFalse();
    }
}
