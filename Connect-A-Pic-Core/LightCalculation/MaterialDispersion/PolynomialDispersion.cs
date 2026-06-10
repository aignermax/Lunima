namespace CAP_Core.LightCalculation.MaterialDispersion;

/// <summary>
/// Polynomial dispersion model for a single-mode waveguide.
/// Computes n_eff(λ), n_g(λ), and loss(λ) using second-order Taylor expansions
/// around a center wavelength:
///   n_eff(λ) = n0 + n1·(λ - λ₀) + n2·(λ - λ₀)²
///   n_g(λ)   = ng0 (constant, or derived from n_eff slope if not supplied)
///   loss(λ)  = loss0 + lossSlope·(λ - λ₀)   [dB/cm]
/// </summary>
public sealed class PolynomialDispersion : IDispersionModel
{
    private readonly double _centerWavelengthNm;
    private readonly double _n0;
    private readonly double _n1;
    private readonly double _n2;
    private readonly double _ng0;
    private readonly bool _hasExplicitNg0;
    private readonly double _loss0;
    private readonly double _lossSlope;

    /// <summary>
    /// Creates a polynomial dispersion model.
    /// </summary>
    /// <param name="centerWavelengthNm">Center wavelength λ₀ in nm (e.g. 1550).</param>
    /// <param name="n0">Effective index at λ₀.</param>
    /// <param name="n1">First-order dispersion coefficient (RIU/nm).</param>
    /// <param name="n2">Second-order dispersion coefficient (RIU/nm²).</param>
    /// <param name="ng0">Group index at λ₀. When null, derived as n0 - λ₀·n1.</param>
    /// <param name="loss0">Propagation loss at λ₀ in dB/cm.</param>
    /// <param name="lossSlope">Linear loss slope in (dB/cm)/nm.</param>
    public PolynomialDispersion(
        double centerWavelengthNm,
        double n0,
        double n1 = 0.0,
        double n2 = 0.0,
        double? ng0 = null,
        double loss0 = 0.5,
        double lossSlope = 0.0)
    {
        if (centerWavelengthNm <= 0)
            throw new ArgumentOutOfRangeException(nameof(centerWavelengthNm), "Center wavelength must be positive.");
        if (loss0 < 0)
            throw new ArgumentOutOfRangeException(nameof(loss0), "Loss must be non-negative.");

        _centerWavelengthNm = centerWavelengthNm;
        _n0 = n0;
        _n1 = n1;
        _n2 = n2;
        _hasExplicitNg0 = ng0.HasValue;
        // n_g = n_eff - λ · dn_eff/dλ; at λ₀: n_g ≈ n0 - λ₀·n1
        _ng0 = ng0 ?? (n0 - centerWavelengthNm * n1);
        _loss0 = loss0;
        _lossSlope = lossSlope;
    }

    /// <inheritdoc/>
    public double NEffAt(double wavelengthNm)
    {
        double delta = wavelengthNm - _centerWavelengthNm;
        return _n0 + _n1 * delta + _n2 * delta * delta;
    }

    /// <inheritdoc/>
    public double GroupIndexAt(double wavelengthNm)
    {
        // n_g(λ) = n_eff(λ) - λ · dn_eff/dλ
        // dn_eff/dλ = n1 + 2·n2·(λ - λ₀)
        double delta = wavelengthNm - _centerWavelengthNm;
        double dnEffDLambda = _n1 + 2.0 * _n2 * delta;
        double nEff = NEffAt(wavelengthNm);
        double derived = nEff - wavelengthNm * dnEffDLambda;

        if (!_hasExplicitNg0)
            return derived;

        // When ng0 was explicitly supplied, use it at the centre wavelength and
        // apply the same dispersion offset as the formula predicts off-centre.
        double derivedAtCenter = _n0 - _centerWavelengthNm * _n1;
        return _ng0 + (derived - derivedAtCenter);
    }

    /// <inheritdoc/>
    public double LossDbPerCmAt(double wavelengthNm)
    {
        double loss = _loss0 + _lossSlope * (wavelengthNm - _centerWavelengthNm);
        return Math.Max(0.0, loss);
    }
}
