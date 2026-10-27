using System.Collections.Immutable;

namespace CAP.Avalonia.ViewModels.Analysis.AnalysisOutput;

/// <summary>Selection values safe to retain across a worker await, without a live component view model.</summary>
internal sealed record CapturedAnalysisOutput(AnalysisOutputState State, string? Name, ImmutableHashSet<Guid>? PinIds)
{
    internal static CapturedAnalysisOutput Capture(AnalysisOutputResolution resolution) => new(resolution.State,
        resolution.Output?.Name, resolution.State == AnalysisOutputState.DesignatedValid
            ? AnalysisOutputResolver.CollectLightPinIds(resolution.Output!).ToImmutableHashSet() : null);
}
