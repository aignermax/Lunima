using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using UnitTests.Analysis.Snapshots;
using UnitTests.Solvers.Lifetime;

namespace UnitTests.Analysis.Lifetime;

internal sealed class ParameterSweepLifetimeFixture : IAsyncDisposable
{
    internal DesignCanvasViewModel Canvas { get; } = new();
    internal ErrorConsoleService Errors { get; } = new();
    internal HeldRunner Runner { get; } = new();
    internal ParameterSweepViewModel Panel { get; }
    internal ComponentViewModel Target { get; }
    internal Guid DownstreamOutput { get; }

    internal ParameterSweepLifetimeFixture(ApplicationWorkCoordinator? application = null, IParameterSweepRunner? runner = null)
    {
        var circuit = new SweepSnapshotFixture();
        DownstreamOutput = circuit.Output;
        circuit.First.AddSlider(0, new Slider(Guid.NewGuid(), 0, 0.5, 1, 0));
        circuit.First.IsUserMarkedLightSource = true;
        circuit.First.LaserEnabled = true;
        Target = new ComponentViewModel(circuit.First);
        Canvas.Components.Add(Target);
        Canvas.Components.Add(new ComponentViewModel(circuit.Second));
        Canvas.RestoreConnectionAndViewModel(circuit.Wire);
        Panel = new ParameterSweepViewModel(Errors, runner ?? Runner, application);
        Panel.ConfigureForComponent(Target, Canvas);
        Panel.StepCount = 3;
    }

    internal Task RunAsync() => Panel.RunSweepCommand.ExecuteAsync(null);

    public async ValueTask DisposeAsync()
    {
        Runner.Barrier.Release.TrySetResult();
        await Panel.StopAsync();
        await Canvas.Document.RetireAsync();
        Canvas.DisposeDocumentBindings();
    }

    internal sealed class HeldRunner : ParameterSweepRunner
    {
        internal RetirementBarrier Barrier { get; } = new();
        internal List<SweepDataPoint>? Completed { get; private set; }
        internal int Calls { get; private set; }

        protected override async Task<List<SweepDataPoint>> SolveSnapshotAsync(ParameterSweepSnapshot snapshot,
            CancellationToken cancellationToken, IProgress<int>? progress)
        {
            Calls++;
            await Barrier.WaitAsync(cancellationToken);
            // Simulate a solver that completes despite cancellation. The panel must still reject stale output.
            return Completed = await base.SolveSnapshotAsync(snapshot, CancellationToken.None, progress);
        }
    }
}
