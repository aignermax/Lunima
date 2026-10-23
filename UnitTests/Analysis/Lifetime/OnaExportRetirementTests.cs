using Avalonia.Headless.XUnit;
using Shouldly;
using UnitTests.Solvers.Lifetime;

namespace UnitTests.Analysis.Lifetime;

public sealed class OnaExportRetirementTests
{
    [AvaloniaFact]
    public async Task Retirement_DrainsPickerWithoutWritingLateSelection()
    {
        await using var fixture = new OnaExportFixture();
        await fixture.Model.RunSweepCommand.ExecuteAsync(null);
        var originalStatus = fixture.Model.StatusText;
        var originalErrors = fixture.Errors.Entries.Count;
        var picker = new RetirementBarrier();
        fixture.HoldPicker(picker);
        var running = fixture.Model.ExportCsvCommand.ExecuteAsync(null);
        var stopping = Task.CompletedTask;
        try
        {
            await picker.AwaitStartAsync(running);
            stopping = fixture.Model.StopAsync();
            stopping.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            fixture.Model.Writer.Release.TrySetResult();
            await picker.FinishAsync(running, stopping);
        }
        fixture.Model.Writes.ShouldBe(0);
        fixture.Model.StatusText.ShouldBe(originalStatus);
        fixture.Errors.Entries.Count.ShouldBe(originalErrors);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retirement_CancelsAndDrainsAcceptedWriteWithoutLateStatus(bool fail)
    {
        await using var fixture = new OnaExportFixture();
        fixture.Model.FailWrite = fail;
        await fixture.Model.RunSweepCommand.ExecuteAsync(null);
        var originalStatus = fixture.Model.StatusText;
        var originalErrors = fixture.Errors.Entries.Count;
        var running = fixture.Model.ExportCsvCommand.ExecuteAsync(null);
        var stopping = Task.CompletedTask;
        try
        {
            await fixture.Model.Writer.AwaitStartAsync(running);
            stopping = fixture.Model.StopAsync();
            await fixture.Model.Writer.AssertDrainingAsync(stopping);
        }
        finally { await fixture.Model.Writer.FinishAsync(running, stopping); }
        fixture.Model.StatusText.ShouldBe(originalStatus);
        fixture.Errors.Entries.Count.ShouldBe(originalErrors);
        fixture.Model.Writes.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task PendingExport_UsesResultSelectedBeforeThePicker()
    {
        await using var fixture = new OnaExportFixture();
        fixture.Model.Writer.Release.TrySetResult();
        await fixture.Model.RunSweepCommand.ExecuteAsync(null);
        var picker = new RetirementBarrier();
        fixture.HoldPicker(picker);
        var exporting = fixture.Model.ExportCsvCommand.ExecuteAsync(null);
        try
        {
            await picker.AwaitStartAsync(exporting);
            fixture.Model.StartNm = 1300;
            await fixture.Model.RunSweepCommand.ExecuteAsync(null);
        }
        finally { await picker.FinishAsync(exporting); }
        fixture.Model.Content.ShouldNotBeNull();
        fixture.Model.Content.Split('\n')[1].ShouldStartWith("1500,");
        fixture.Model.Writes.ShouldBe(1);
    }
}
