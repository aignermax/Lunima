using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.ViewModels.Analysis.MonteCarloAnalysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core;
using CAP_Core.Analysis.MonteCarloAnalysis;
using UnitTests.Analysis.Snapshots;
using UnitTests.Solvers.Lifetime;

namespace UnitTests.Analysis.Lifetime;

internal sealed class MonteCarloLifetimeFixture : IAsyncDisposable
{
    internal DesignCanvasViewModel Canvas { get; } = new();
    internal DesignCanvasViewModel OtherCanvas { get; } = new();
    internal ErrorConsoleService Errors { get; } = new();
    internal HeldRunner Runner { get; } = new();
    internal MonteCarloViewModel Panel { get; }

    internal MonteCarloLifetimeFixture(ApplicationWorkCoordinator? application = null, bool eye = false)
    {
        var circuit = new SweepSnapshotFixture();
        circuit.First.IsUserMarkedLightSource = true;
        circuit.First.LaserEnabled = true;
        circuit.Second.IsUserMarkedLightSource = true;
        circuit.Second.LaserEnabled = false;
        Canvas.Components.Add(new ComponentViewModel(circuit.First));
        Canvas.Components.Add(new ComponentViewModel(circuit.Second));
        Canvas.RestoreConnectionAndViewModel(circuit.Wire);
        Panel = new(Errors, Runner, application, Runner) { RunCount = 3, StartNm = 1549, EndNm = 1551, StepCount = 3 };
        if (eye) Panel.SelectedMetric = Panel.Metrics[1];
        Panel.Configure(Canvas);
    }

    internal Task RunAsync() => Panel.RunAnalysisCommand.ExecuteAsync(null);
    internal void Change(string change)
    {
        if (change == "edit") Canvas.Document.Invalidate();
        else if (change == "replace") Canvas.Document.ReplaceDocument();
        else if (change == "configure") Panel.Configure(OtherCanvas);
        else throw new ArgumentOutOfRangeException(nameof(change));
    }

    public async ValueTask DisposeAsync()
    {
        Runner.Barrier.Release.TrySetResult();
        if (Panel.RunAnalysisCommand.ExecutionTask is { } running) await running.WaitAsync(TimeSpan.FromSeconds(10));
        await Panel.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await Task.WhenAll(Canvas.Document.RetireAsync(), OtherCanvas.Document.RetireAsync());
        Canvas.DisposeDocumentBindings();
        OtherCanvas.DisposeDocumentBindings();
    }

    internal sealed class HeldRunner : MonteCarloSpectrumRunner, IMonteCarloEyeRunner
    {
        internal RetirementBarrier Barrier { get; } = new();
        internal int Calls { get; private set; }
        internal bool Fail { get; set; }
        internal bool HonorCancellation { get; set; }
        internal IProgress<MonteCarloProgress>? Progress { get; private set; }

        public override async Task<MonteCarloSpectrumResult> RunAsync(MonteCarloSpectrumSnapshot snapshot,
            IProgress<MonteCarloProgress>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            Progress = progress;
            await Barrier.WaitAsync(cancellationToken);
            if (HonorCancellation) cancellationToken.ThrowIfCancellationRequested();
            if (Fail) throw new InvalidOperationException("late Monte Carlo failure");
            // Deliver real captured results after the cancellation checkpoint, like a worker
            // already returning when the caller closes or replaces its document.
            return await Task.Run(() => snapshot.SolveAsync());
        }

        public async Task<MonteCarloResult> RunAsync(MonteCarloEyeSnapshot snapshot,
            IProgress<MonteCarloProgress>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            Progress = progress;
            await Barrier.WaitAsync(cancellationToken);
            if (HonorCancellation) cancellationToken.ThrowIfCancellationRequested();
            if (Fail) throw new InvalidOperationException("late Monte Carlo eye failure");
            return await Task.Run(() => snapshot.SolveAsync());
        }
    }
}
