using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Lifetime;
using Moq;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class TransientPanelRetirementTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Retirement_DrainsTheActualWorkerAndPreventsLaterAdmission(bool eye, bool applicationStop)
    {
        var application = new ApplicationWorkCoordinator();
        await using var fixture = new TransientPanelFixture(eye, application);
        var running = fixture.RunAsync();
        var stopping = Task.CompletedTask;
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            stopping = applicationStop ? application.StopAsync() : fixture.StopAsync();
            await fixture.Runner.Barrier.AssertDrainingAsync(stopping);
        }
        finally { await fixture.Runner.Barrier.FinishAsync(running, stopping); }
        fixture.HasResult.ShouldBeFalse();
        fixture.Errors.Entries.ShouldBeEmpty();
        await fixture.RunAsync();
        fixture.Runner.Calls.ShouldBe(1);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReconfiguredRun_CannotPublishLateSuccessOrFailure(bool eye, bool fail)
    {
        await using var fixture = new TransientPanelFixture(eye);
        fixture.Runner.Fail = fail;
        var running = fixture.RunAsync();
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            fixture.Configure(null);
        }
        finally { await fixture.Runner.Barrier.FinishAsync(running); }
        fixture.HasResult.ShouldBeFalse();
        fixture.Status.ShouldBeEmpty();
        fixture.Text.ShouldBeEmpty();
        fixture.Errors.Entries.ShouldBeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkerUsesCapturedCircuitAndSettingsAfterLiveDefinitionsAreRemoved(bool eye)
    {
        await using var fixture = new TransientPanelFixture(eye);
        var running = fixture.RunAsync();
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            foreach (var component in fixture.Canvas.Components)
            {
                component.Component.WaveLengthToSMatrixMap.Clear();
                component.Component.PhysicalPins.Clear();
            }
            if (eye) fixture.Eye!.BitRateGbps = -1;
            else
            {
                fixture.Transient!.FreqPoints = -1;
                fixture.Transient.PulseSigmaPs = -1;
                fixture.Transient.Source.BitrateGbps = -1;
            }
            fixture.Canvas.Document.Invalidate();
        }
        finally { await fixture.Runner.Barrier.FinishAsync(running); }
        if (eye)
        {
            fixture.Runner.EyeResult.ShouldNotBeNull();
            fixture.Runner.EyeResult.Error.ShouldBeNull();
            fixture.Runner.EyeResult.Histogram.ShouldNotBeNull();
        }
        else
        {
            fixture.Runner.TransientResult.ShouldNotBeNull();
            fixture.Runner.TransientResult.Result.PinTraces[fixture.OutputId].Max().ShouldBeGreaterThan(0);
        }
        fixture.HasResult.ShouldBeFalse();
        fixture.Errors.Entries.ShouldBeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReconfigureDuringCsvPicker_DoesNotWriteOrPublishAnOldExport(bool eye)
    {
        await using var fixture = new TransientPanelFixture(eye);
        fixture.Runner.Barrier.Release.TrySetResult();
        await fixture.RunAsync().WaitAsync(TimeSpan.FromSeconds(15));
        fixture.HasResult.ShouldBeTrue();
        var selected = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs = new Mock<IFileDialogService>();
        dialogs.Setup(dialog => dialog.ShowSaveFileDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(selected.Task);
        fixture.SetDialog(dialogs.Object);
        var path = Path.Combine(Path.GetTempPath(), $"lunima-stale-transient-{Guid.NewGuid():N}.csv");
        var exporting = fixture.ExportAsync();
        try
        {
            dialogs.Verify(dialog => dialog.ShowSaveFileDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            fixture.Configure(null);
        }
        finally
        {
            selected.TrySetResult(path);
            await exporting.WaitAsync(TimeSpan.FromSeconds(10));
        }
        try
        {
            File.Exists(path).ShouldBeFalse();
            fixture.Status.ShouldBeEmpty();
            fixture.Errors.Entries.ShouldBeEmpty();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
