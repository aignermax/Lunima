namespace CAP_Core.LightCalculation.MaterialDispersion;

/// <summary>
/// Constant (wavelength-independent) dispersion model.
/// Used as the backwards-compatible fallback for PDKs that do not declare a
/// materialDispersion block. All quantities are fixed scalars independent of λ.
/// </summary>
public sealed class ConstantDispersion : IDispersionModel
{
    private readonly double _nEff;
    private readonly double _groupIndex;
    private readonly double _lossDbPerCm;

    /// <summary>
    /// Creates a constant dispersion model with fixed parameters.
    /// </summary>
    /// <param name="nEff">Effective refractive index (wavelength-independent).</param>
    /// <param name="groupIndex">Group index (wavelength-independent).</param>
    /// <param name="lossDbPerCm">Propagation loss in dB/cm (wavelength-independent).</param>
    public ConstantDispersion(double nEff = 2.45, double groupIndex = 4.2, double lossDbPerCm = 0.5)
    {
        if (lossDbPerCm < 0)
            throw new ArgumentOutOfRangeException(nameof(lossDbPerCm), "Loss must be non-negative.");

        _nEff = nEff;
        _groupIndex = groupIndex;
        _lossDbPerCm = lossDbPerCm;
    }

    /// <inheritdoc/>
    public double NEffAt(double wavelengthNm) => _nEff;

    /// <inheritdoc/>
    public double GroupIndexAt(double wavelengthNm) => _groupIndex;

    /// <inheritdoc/>
    public double LossDbPerCmAt(double wavelengthNm) => _lossDbPerCm;
}
