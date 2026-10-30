using System.ComponentModel;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Analysis.OnaAnalysis;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class SweepDocumentCleanupTests
{
    [AvaloniaTheory]
    [InlineData(false, "document")]
    [InlineData(true, "document")]
    [InlineData(false, "owner")]
    [InlineData(true, "owner")]
    [InlineData(false, "user")]
    [InlineData(true, "user")]
    public async Task Retirement_AwaitsCancellationCallbackAfterWorkerReturns(bool spectrum, string origin)
    {
        var canvas = new DesignCanvasViewModel();
        var source = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        source.IsUserMarkedLightSource = true;
        source.LaserEnabled = true;
        canvas.Components.Add(new ComponentViewModel(source));
        using var runner = new CallbackSweep();
        var wavelength = spectrum ? new WavelengthSpectrumViewModel(new CAP_Core.Analysis.MeasuredSpectrum.MeasuredSpectrumFileSource(), runner: runner) : null;
        var ona = spectrum ? null : new OnaSweepViewModel(runner: runner);
        wavelength?.Configure(canvas);
        ona?.Configure(canvas);
        var workerHandled = Signal();
        INotifyPropertyChanged model = wavelength as INotifyPropertyChanged ?? ona!;
        PropertyChangedEventHandler handled = (_, args) =>
        {
            if (args.PropertyName == nameof(OnaSweepViewModel.IsSweeping)
                && !(wavelength?.IsSweeping ?? ona!.IsSweeping)) workerHandled.TrySetResult();
        };
        model.PropertyChanged += handled;
        Task Stop() => wavelength?.StopAsync() ?? ona!.StopAsync();
        var running = wavelength?.RunSweepCommand.ExecuteAsync(null) ?? ona!.RunSweepCommand.ExecuteAsync(null);
        var stopping = Task.CompletedTask;
        try
        {
            await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (origin == "document") canvas.Document.Invalidate();
            else if (origin == "owner") stopping = Stop();
            else if (wavelength != null) wavelength.CancelSweepCommand.Execute(null);
            else ona!.CancelSweepCommand.Execute(null);
            await runner.CallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            runner.CallbackTimedOut.ShouldBeFalse("requesting cancellation must not block the UI thread");
            runner.ReturnResult.TrySetResult();
            // The async signal resumes after the worker continuation's synchronous
            // finally steps, without waiting for the document callback itself.
            await workerHandled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            runner.CallbackTimedOut.ShouldBeFalse(
                "lease cleanup must yield the UI thread while its cancellation observer remains blocked");
            stopping = Stop();
            stopping.IsCompleted.ShouldBeFalse(
                "window/panel retirement must include the lease's pending cancellation cleanup");
            runner.CallbackFinished.Task.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            runner.ReleaseCallback.Set();
            runner.ReturnResult.TrySetResult();
            await Task.WhenAll(running, stopping, Stop()).WaitAsync(TimeSpan.FromSeconds(5));
            await canvas.Document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
            model.PropertyChanged -= handled;
            canvas.DisposeDocumentBindings();
        }
        runner.CallbackFinished.Task.IsCompleted.ShouldBeTrue();
        runner.CallbackTimedOut.ShouldBeFalse();
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class CallbackSweep : IWavelengthSweepRunner, IDisposable
    {
        private CancellationTokenRegistration _registration;
        public TaskCompletionSource Started { get; } = Signal();
        public TaskCompletionSource CallbackEntered { get; } = Signal();
        public TaskCompletionSource CallbackFinished { get; } = Signal();
        public TaskCompletionSource ReturnResult { get; } = Signal();
        public ManualResetEventSlim ReleaseCallback { get; } = new();
        public bool CallbackTimedOut { get; private set; }

        public async Task<WavelengthSweepResult> RunAsync(WavelengthSweepConfiguration configuration,
            GridManager grid, IExternalPortManager ports, CancellationToken cancellationToken)
        {
            _registration = cancellationToken.Register(() =>
            {
                CallbackEntered.TrySetResult();
                CallbackTimedOut = !ReleaseCallback.Wait(TimeSpan.FromSeconds(10));
                CallbackFinished.TrySetResult();
            });
            Started.TrySetResult();
            await ReturnResult.Task;
            // Some integrations complete their Task while an independently scheduled
            // cancellation observer is still unwinding. The lease owns that observer.
            return new WavelengthSweepResult(configuration, [], [], []);
        }

        public void Dispose()
        {
            ReleaseCallback.Set();
            _registration.Dispose();
            ReleaseCallback.Dispose();
        }
    }
}
