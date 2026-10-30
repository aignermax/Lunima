using CAP_Core.Analysis.MonteCarloAnalysis;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using CAP_Core.LightCalculation;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Analysis.AnalysisOutput;
using CAP.Avalonia.ViewModels.Analysis.EyeDiagram;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Analysis.MonteCarloAnalysis;

/// <summary>Captures the existing Monte Carlo eye metric before scheduling any sample.</summary>
internal sealed class MonteCarloEyeSampler
{
    private readonly MonteCarloEyeMetric _metric;
    private readonly Func<ISystemMatrixBuilder, ISystemMatrixBuilder>? _decorateBuilder;

    public MonteCarloEyeSampler(DesignCanvasViewModel canvas, AnalysisOutputResolution resolution,
        Func<ISystemMatrixBuilder, ISystemMatrixBuilder>? decorateBuilder = null)
    {
        var (circuit, excitation) = TransientCircuitFactory.Capture(canvas);
        var designated = resolution.State == AnalysisOutputState.DesignatedValid
            ? AnalysisOutputResolver.CollectLightPinIds(resolution.Output!) : null;
        _metric = new(circuit, excitation, designated, resolution.State == AnalysisOutputState.MultipleCandidates,
            new(EyeTraceSelector.NoSignalAtDesignatedOutputError, EyeTraceSelector.BelowNoiseFloorError,
                EyeTraceSelector.MultipleOutputsWarning), LocalizationService.Instance.Translate("Analysis.Common.NoLaserOn"));
        _decorateBuilder = decorateBuilder;
    }

    internal MonteCarloEyeSnapshot Capture(MonteCarloConfiguration configuration,
        IReadOnlyList<Component> participants, ProcessTolerances tolerances) =>
        MonteCarloEyeSnapshot.Capture(configuration, _metric, participants, tolerances);

    /// <summary>Runs one captured metric with a caller-supplied, worker-owned decorator.</summary>
    public Task<double[]> SampleAsync(CancellationToken cancellationToken) =>
        Task.Run(() => new[] { _metric.Solve(cancellationToken, _decorateBuilder) }, cancellationToken);
}
