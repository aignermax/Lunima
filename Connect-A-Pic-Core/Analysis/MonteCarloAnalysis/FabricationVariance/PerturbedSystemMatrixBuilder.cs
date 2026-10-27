using CAP_Core.LightCalculation;

namespace CAP_Core.Analysis.MonteCarloAnalysis.FabricationVariance;

/// <summary>Legacy live-component adapter for the shared fabrication perturbation kernel.</summary>
public sealed class PerturbedSystemMatrixBuilder : ISystemMatrixBuilder
{
    /// <summary>Physical length of one grid tile in micrometers.</summary>
    public const double NominalTileLengthUm = 250;
    private readonly FabricationMatrixPerturbation _kernel;

    public PerturbedSystemMatrixBuilder(ISystemMatrixBuilder inner, FabricationVarianceSource source)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(source);
        _kernel = new(inner, new LiveFabricationState(source));
    }

    public SMatrix GetSystemSMatrix(int LaserWaveLengthInNm) => _kernel.GetSystemSMatrix(LaserWaveLengthInNm);
    public SMatrix GetSystemSMatrix(double wavelengthNm) => _kernel.GetSystemSMatrix(wavelengthNm);
}
