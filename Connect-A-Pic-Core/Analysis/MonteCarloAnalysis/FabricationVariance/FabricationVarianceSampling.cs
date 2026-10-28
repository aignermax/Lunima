using CAP_Core.Components.Process;

namespace CAP_Core.Analysis.MonteCarloAnalysis.FabricationVariance;

/// <summary>One shared draw order for live and detached fabrication sources.</summary>
internal static class FabricationVarianceSampling
{
    internal static Dictionary<T, ComponentDeviation> Draw<T>(IReadOnlyList<T> participants,
        ProcessTolerances tolerances, GaussianSampler sampler) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(sampler);
        var waferWidth = sampler.NextGaussian() * tolerances.WidthSigmaNm;
        var waferThickness = sampler.NextGaussian() * tolerances.ThicknessSigmaNm;
        var deviations = new Dictionary<T, ComponentDeviation>();
        // Keep duplicates and order: every occurrence consumes two local draws, and the
        // last occurrence of the same component owns its final deviation.
        foreach (var participant in participants)
            deviations[participant] = new(
                waferWidth + sampler.NextGaussian() * FabricationVarianceSource.LocalSigmaFraction * tolerances.WidthSigmaNm,
                waferThickness + sampler.NextGaussian() * FabricationVarianceSource.LocalSigmaFraction * tolerances.ThicknessSigmaNm);
        return deviations;
    }
}
