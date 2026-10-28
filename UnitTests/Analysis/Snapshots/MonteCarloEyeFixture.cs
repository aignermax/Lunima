using CAP.Avalonia.ViewModels.Analysis.AnalysisOutput;
using CAP.Avalonia.ViewModels.Analysis.MonteCarloAnalysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.MonteCarloAnalysis.FabricationVariance;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Process;
using CAP_Core.LightCalculation;

namespace UnitTests.Analysis.Snapshots;

internal sealed class MonteCarloEyeFixture : IAsyncDisposable
{
    internal SweepSnapshotFixture Circuit { get; } = new(coherent: true);
    internal DesignCanvasViewModel Canvas { get; } = new();
    internal ComponentViewModel Input { get; }
    internal ComponentViewModel Output { get; }
    internal Guid SourceForwardFlow => Circuit.First.PhysicalPins[1].LogicalPin.IDOutFlow;
    internal AnalysisOutputResolution Resolution => AnalysisOutputResolver.Resolve(Canvas);
    internal IReadOnlyList<Component> Participants => [Circuit.First, Circuit.Second];

    internal MonteCarloEyeFixture(bool withAttenuator = false)
    {
        Circuit.First.IsUserMarkedLightSource = true;
        Circuit.First.LaserEnabled = true;
        Circuit.Second.IsUserMarkedLightSource = true;
        Circuit.Second.LaserEnabled = false;
        Input = new(Circuit.First);
        Output = new(Circuit.Second);
        Canvas.Components.Add(Input);
        Canvas.Components.Add(Output);
        if (withAttenuator)
        {
            var attenuator = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
            attenuator.Name = "separate attenuator";
            foreach (var matrix in attenuator.WaveLengthToSMatrixMap.Values.Distinct())
                matrix.SetValues(matrix.GetNonNullValues().ToDictionary(pair => pair.Key, pair => pair.Value * 0.75), reset: true);
            Canvas.Components.Add(new ComponentViewModel(attenuator));
            Circuit.Wire.EndPin = attenuator.PhysicalPins[0];
            // Isolate output selection from propagation delay and its folded-eye spread.
            // Route-loss/phase capture has separate controls in the same test class.
            Circuit.Wire.RestoreCachedPath(SweepSnapshotFixture.Path(0));
            var downstream = new WaveguideConnection
                { StartPin = attenuator.PhysicalPins[1], EndPin = Circuit.Second.PhysicalPins[0] };
            downstream.RestoreCachedPath(SweepSnapshotFixture.Path(0));
            Canvas.RestoreConnectionAndViewModel(downstream);
        }
        Canvas.RestoreConnectionAndViewModel(Circuit.Wire);
        Canvas.ConnectionManager.EnableCoherentPropagationPhase = true;
    }

    internal MonteCarloEyeSampler Capture(Func<ISystemMatrixBuilder, ISystemMatrixBuilder>? decorate = null)
        => new(Canvas, Resolution, decorate);

    internal FabricationVarianceSource CreateVariance() => new(Participants, new ProcessTolerances(3, 1));

    public async ValueTask DisposeAsync()
    {
        await Canvas.Document.RetireAsync();
        Canvas.DisposeDocumentBindings();
    }
}
