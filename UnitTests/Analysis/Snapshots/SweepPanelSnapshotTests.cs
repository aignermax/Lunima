using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Analysis.OnaAnalysis;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core;
using CAP_Core.Analysis.OnaAnalysis;
using Shouldly;

namespace UnitTests.Analysis.Snapshots;

public sealed class SweepPanelSnapshotTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealRunner_CapturesBeforeWorkerAndPanelsKeepOriginalPhysics(bool spectrum)
    {
        var canvas = new DesignCanvasViewModel();
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        component.IsUserMarkedLightSource = true;
        component.LaserEnabled = true;
        canvas.Components.Add(new ComponentViewModel(component));
        var errors = new ErrorConsoleService();
        var runner = new ControlledRunner();
        await using var ona = new OnaSweepViewModel(errors, runner);
        await using var spectrumPanel = new WavelengthSpectrumViewModel(new CAP_Core.Analysis.MeasuredSpectrum.MeasuredSpectrumFileSource(), errors, runner);
        ona.StepCount = spectrumPanel.StepCount = 3;
        ona.Configure(canvas);
        spectrumPanel.Configure(canvas);
        var running = spectrum ? spectrumPanel.RunSweepCommand.ExecuteAsync(null) : ona.RunSweepCommand.ExecuteAsync(null);
        var published = false;
        try
        {
            await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Deliberately bypass document notifications: ownership guards alone cannot provide input isolation.
            foreach (var matrix in component.WaveLengthToSMatrixMap.Values) matrix.SetValues(new(), reset: true);
            component.WaveLengthToSMatrixMap.Clear();
        }
        finally
        {
            runner.Release.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(10));
            published = spectrum ? spectrumPanel.HasResult : ona.HasResult;
            await ona.StopAsync();
            await spectrumPanel.StopAsync();
            await canvas.Document.RetireAsync();
            canvas.DisposeDocumentBindings();
        }
        runner.Completed.ShouldNotBeNull("the real detached solver must not consult the removed live matrices");
        published.ShouldBeTrue();
        runner.Completed.DataPoints.ShouldAllBe(point => point.OutputPowers.Values.Any(power => power > 0));
        errors.Entries.ShouldNotContain(entry => entry.Message.Contains("failed", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ControlledRunner : WavelengthSweepRunner
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal WavelengthSweepResult? Completed { get; private set; }

        protected override async Task<WavelengthSweepResult> SolveSnapshotAsync(
            WavelengthSweepSnapshot snapshot, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return Completed = await base.SolveSnapshotAsync(snapshot, cancellationToken);
        }
    }
}
