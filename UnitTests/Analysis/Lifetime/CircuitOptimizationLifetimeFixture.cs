using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Analysis.CircuitOptimization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.CircuitOptimization;
using UnitTests.Analysis.Snapshots;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

internal sealed class CircuitOptimizationLifetimeFixture : IAsyncDisposable
{
    internal DesignCanvasViewModel Canvas { get; } = new();
    internal CommandManager Commands { get; } = new();
    internal HeldRunner Runner { get; } = new();
    internal CircuitOptimizationViewModel Panel { get; }
    internal ComponentViewModel Target { get; }

    internal CircuitOptimizationLifetimeFixture()
    {
        Target = Populate(Canvas, 0.2);
        Panel = new(Commands, runner: Runner);
        Panel.ConfigureForCanvas(Canvas);
        Panel.SelectedTarget = new("output", [Target.Component.PhysicalPins[1].LogicalPin.IDOutFlow]);
    }

    internal static ComponentViewModel Populate(DesignCanvasViewModel canvas, double initial)
    {
        var (circuit, slider, _) = CircuitOptimizationSnapshotTests.Create();
        slider.Value = initial;
        circuit.First.IsUserMarkedLightSource = true;
        circuit.First.LaserEnabled = true;
        var target = new ComponentViewModel(circuit.First);
        canvas.Components.Add(target);
        canvas.Components.Add(new ComponentViewModel(circuit.Second));
        canvas.RestoreConnectionAndViewModel(circuit.Wire);
        canvas.ConnectionManager.Connections.ShouldContain(circuit.Wire);
        return target;
    }

    internal Task RunAsync() => Panel.RunOptimizationCommand.ExecuteAsync(null);
    internal async Task FinishAsync(Task running)
    {
        Runner.Release.TrySetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(10));
    }

    public async ValueTask DisposeAsync()
    {
        Runner.Release.TrySetResult();
        if (Panel.RunOptimizationCommand.ExecutionTask is { } running) await running;
        await Panel.StopAsync();
        await Canvas.Document.RetireAsync();
        Canvas.DisposeDocumentBindings();
    }

    internal sealed class HeldRunner : CircuitOptimizationRunner
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool FailOnRelease { get; set; }
        internal IProgress<OptimizationProgress>? Progress { get; private set; }
        internal CancellationToken Token { get; private set; }
        internal int Calls { get; private set; }
        internal CircuitOptimizationRunResult Result { get; set; } =
            new(1, [new OptimizationCandidate(new[] { 0.8 }, 2)], 12, false);

        public override async Task<CircuitOptimizationRunResult> RunAsync(CircuitOptimizationSnapshot snapshot,
            CancellationToken cancellationToken, IProgress<OptimizationProgress>? progress = null)
        {
            Calls++;
            Token = cancellationToken;
            Progress = progress;
            Started.TrySetResult();
            await Release.Task;
            if (FailOnRelease) throw new InvalidOperationException("obsolete optimizer failure");
            // Like a native worker already returning, deliberately deliver after cancellation.
            return Result;
        }
    }
}
