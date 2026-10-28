using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Analysis.EyeDiagram;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core;
using CAP_Core.Analysis.EyeDiagram;
using CAP_Core.LightCalculation.TimeDomainSimulation;
using Shouldly;
using UnitTests.Analysis.Snapshots;
using UnitTests.Solvers.Lifetime;

namespace UnitTests.Analysis.Lifetime;

internal sealed class TransientPanelFixture : IAsyncDisposable
{
    internal DesignCanvasViewModel Canvas { get; } = new();
    internal ErrorConsoleService Errors { get; } = new();
    internal HeldRunner Runner { get; } = new();
    internal ComponentViewModel Input { get; }
    internal Guid OutputId { get; }
    internal TimeDomainViewModel? Transient { get; }
    internal EyeDiagramViewModel? Eye { get; }
    internal bool HasResult => Eye?.HasResult ?? Transient!.HasResult;
    internal string Status => Eye?.StatusText ?? Transient!.StatusText;
    internal string Text => Eye?.MetricsText ?? Transient!.ResultText;

    internal TransientPanelFixture(bool eye, ApplicationWorkCoordinator? application = null)
    {
        var circuit = new SweepSnapshotFixture();
        circuit.First.IsUserMarkedLightSource = true;
        circuit.First.LaserEnabled = true;
        circuit.Second.IsUserMarkedLightSource = true;
        circuit.Second.LaserEnabled = false;
        Input = new ComponentViewModel(circuit.First);
        Canvas.Components.Add(Input);
        Canvas.Components.Add(new ComponentViewModel(circuit.Second));
        Canvas.RestoreConnectionAndViewModel(circuit.Wire);
        Canvas.ConnectionManager.Connections.ShouldContain(circuit.Wire);
        OutputId = circuit.Output;
        if (eye) Eye = new EyeDiagramViewModel(Errors, Runner, application) { BitRateGbps = 100 };
        else Transient = new TimeDomainViewModel(Errors, Runner, application) { FreqPoints = 64 };
        Configure(Canvas);
    }

    internal void Configure(DesignCanvasViewModel? canvas)
    {
        if (Eye != null) Eye.Configure(canvas); else Transient!.Configure(canvas);
    }
    internal Task RunAsync() => Eye?.RunEyeAnalysisCommand.ExecuteAsync(null)
        ?? Transient!.RunTransientCommand.ExecuteAsync(null);
    internal Task StopAsync() => Eye?.StopAsync() ?? Transient!.StopAsync();
    internal Task ExportAsync() => Eye?.ExportCsvCommand.ExecuteAsync(null) ?? Transient!.ExportCsvCommand.ExecuteAsync(null);
    internal void SetDialog(IFileDialogService dialog)
    {
        if (Eye != null) Eye.FileDialogService = dialog; else Transient!.FileDialogService = dialog;
    }
    public async ValueTask DisposeAsync()
    {
        Runner.Barrier.Release.TrySetResult();
        await StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await Canvas.Document.RetireAsync();
        Canvas.DisposeDocumentBindings();
    }

    internal sealed class HeldRunner : ITransientSimulationRunner, IEyeAnalysisRunner
    {
        internal RetirementBarrier Barrier { get; } = new();
        internal int Calls { get; private set; }
        internal bool Fail { get; set; }
        internal TransientSimulationOutcome? TransientResult { get; private set; }
        internal EyeAnalysisOutcome? EyeResult { get; private set; }
        internal TransientSimulationRequest? TransientInput { get; private set; }
        internal EyeAnalysisRequest? EyeInput { get; private set; }

        public async Task<TransientSimulationOutcome> RunAsync(TransientSimulationRequest request, CancellationToken token)
        {
            Calls++;
            TransientInput = request;
            await Barrier.WaitAsync(token);
            if (Fail) throw new InvalidOperationException("late transient failure");
            return TransientResult = await Task.Run(() => request.Solve());
        }

        public async Task<EyeAnalysisOutcome> RunAsync(EyeAnalysisRequest request, CancellationToken token)
        {
            Calls++;
            EyeInput = request;
            await Barrier.WaitAsync(token);
            if (Fail) throw new InvalidOperationException("late eye failure");
            return EyeResult = await Task.Run(() => request.Solve());
        }
    }
}
