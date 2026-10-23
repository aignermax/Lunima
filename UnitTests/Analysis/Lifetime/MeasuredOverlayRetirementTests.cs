using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP_Core;
using CAP_Core.Analysis.MeasuredSpectrum;
using Moq;
using Shouldly;
using UnitTests.Solvers.Lifetime;
using SpectrumData = CAP_Core.Analysis.MeasuredSpectrum.MeasuredSpectrum;

namespace UnitTests.Analysis.Lifetime;

public sealed class MeasuredOverlayRetirementTests
{
    [AvaloniaFact]
    public async Task SpectrumRetirement_DrainsPickerAndRejectsItsLateSelection()
    {
        await using var fixture = new SweepLifetimeFixture(spectrum: true);
        var overlay = fixture.Spectrum!.Overlay;
        var barrier = new RetirementBarrier();
        var files = new Mock<IFileDialogService>();
        files.Setup(service => service.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(async () => { await barrier.WaitAsync(CancellationToken.None); return "missing-late-measurement.csv"; });
        overlay.FileDialogService = files.Object;
        var running = overlay.LoadMeasuredCommand.ExecuteAsync(null);
        var stopping = Task.CompletedTask;
        try
        {
            await barrier.AwaitStartAsync(running);
            stopping = fixture.StopAsync();
            stopping.IsCompleted.ShouldBeFalse("the picker has not returned yet");
        }
        finally { await barrier.FinishAsync(running, stopping); }
        overlay.Spectrum.ShouldBeNull();
        overlay.ErrorText.ShouldBeEmpty();
        fixture.Errors.Entries.ShouldBeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retirement_CancelsAndDrainsReadWithoutLatePublication(bool fail)
    {
        var errors = new ErrorConsoleService();
        var overlay = new ControlledOverlay(errors) { Fail = fail };
        var changed = 0;
        overlay.OverlayChanged += (_, _) => changed++;
        var running = overlay.LoadFromFileAsync("measurement.csv");
        var stopping = Task.CompletedTask;
        try
        {
            await overlay.Barrier.AwaitStartAsync(running);
            stopping = overlay.StopAsync();
            // This reader receives the owner's token directly. CancelAsync changes
            // its state synchronously but schedules callbacks on the thread pool;
            // callback dispatch latency is not the lifetime contract under test.
            overlay.ReadToken.IsCancellationRequested.ShouldBeTrue("retirement reaches the actual reader token");
            stopping.IsCompleted.ShouldBeFalse("the accepted reader has not completed its cleanup");
        }
        finally { await overlay.Barrier.FinishAsync(running, stopping); }
        overlay.HasOverlay.ShouldBeFalse();
        overlay.IsLoading.ShouldBeFalse();
        overlay.ErrorText.ShouldBeEmpty();
        errors.Entries.ShouldBeEmpty();
        changed.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task RetiredOverlay_RejectsPickerAndDirectRead()
    {
        var overlay = new ControlledOverlay(new ErrorConsoleService());
        overlay.Barrier.Release.TrySetResult();
        var files = new Mock<IFileDialogService>();
        overlay.FileDialogService = files.Object;
        await overlay.StopAsync();
        await overlay.LoadMeasuredCommand.ExecuteAsync(null);
        await overlay.LoadFromFileAsync("measurement.csv");
        overlay.Reads.ShouldBe(0);
        files.Invocations.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task OrdinaryRead_PublishesMeasuredOverlay()
    {
        var overlay = new ControlledOverlay(new ErrorConsoleService());
        overlay.Barrier.Release.TrySetResult();
        try
        {
            await overlay.LoadFromFileAsync("measurement.csv");
            overlay.HasOverlay.ShouldBeTrue();
            overlay.Spectrum.ShouldNotBeNull();
            overlay.Reads.ShouldBe(1);
        }
        finally { await overlay.StopAsync(); }
    }

    private sealed class ControlledOverlay(ErrorConsoleService errors) : MeasuredSpectrumOverlayViewModel(errors)
    {
        public RetirementBarrier Barrier { get; } = new();
        public int Reads { get; private set; }
        public CancellationToken ReadToken { get; private set; }
        public bool Fail { get; init; }

        protected override async Task<SpectrumData> ReadSpectrumAsync(string path, CancellationToken cancellationToken)
        {
            Reads++;
            ReadToken = cancellationToken;
            await Barrier.WaitAsync(cancellationToken);
            if (Fail) throw new IOException("Late controlled read failure");
            return new SpectrumData([1500d, 1550d, 1600d], [0.1d, 0.9d, 0.1d], "controlled measurement");
        }
    }
}
