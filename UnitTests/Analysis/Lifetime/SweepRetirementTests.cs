using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services.Lifetime;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class SweepRetirementTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retirement_CancelsActualSweepAndDrainsBeforeRejectingItsResult(bool spectrum)
    {
        await using var fixture = new SweepLifetimeFixture(spectrum);
        var running = fixture.RunAsync();
        var stopping = Task.CompletedTask;
        var status = string.Empty;
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            status = fixture.Status;
            stopping = fixture.StopAsync();
            await fixture.Runner.Barrier.AssertDrainingAsync(stopping);
        }
        finally { await fixture.Runner.Barrier.FinishAsync(running, stopping); }
        fixture.HasResult.ShouldBeFalse();
        fixture.IsRunning.ShouldBeFalse();
        fixture.Status.ShouldBe(status);
        fixture.Errors.Entries.ShouldBeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetiredPanel_RejectsNewSweep(bool spectrum)
    {
        await using var fixture = new SweepLifetimeFixture(spectrum);
        fixture.Runner.Barrier.Release.TrySetResult();
        await fixture.StopAsync();
        await fixture.RunAsync();
        fixture.Runner.Calls.ShouldBe(0);
    }

    [AvaloniaTheory]
    [InlineData(false, "replace")]
    [InlineData(true, "replace")]
    [InlineData(false, "edit")]
    [InlineData(true, "edit")]
    [InlineData(false, "configure")]
    [InlineData(true, "configure")]
    public async Task SupersededSweep_CannotPublishResultsOrDiagnostics(bool spectrum, string change)
    {
        await using var fixture = new SweepLifetimeFixture(spectrum);
        var running = fixture.RunAsync();
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            if (change == "replace") fixture.Canvas.Document.ReplaceDocument();
            else if (change == "edit") fixture.Canvas.Document.Invalidate();
            else fixture.Reconfigure();
        }
        finally { await fixture.Runner.Barrier.FinishAsync(running); }
        fixture.HasResult.ShouldBeFalse();
        fixture.Errors.Entries.ShouldBeEmpty();
        fixture.IsRunning.ShouldBeFalse();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplicationRetirement_OwnsActualSweepCleanup(bool spectrum)
    {
        var application = new ApplicationWorkCoordinator();
        await using var fixture = new SweepLifetimeFixture(spectrum, application);
        var running = fixture.RunAsync();
        var stopping = Task.CompletedTask;
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            stopping = application.StopAsync();
            await fixture.Runner.Barrier.AssertDrainingAsync(stopping);
        }
        finally { await fixture.Runner.Barrier.FinishAsync(running, stopping); }
        fixture.HasResult.ShouldBeFalse();
        fixture.Errors.Entries.ShouldBeEmpty();
    }
}
