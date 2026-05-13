namespace CAP_Core.LightCalculation.MaterialDispersion;

/// <summary>
/// Strategy interface for wavelength-dependent material dispersion models.
/// Provides n_eff(λ), n_g(λ), and propagation loss(λ) at an arbitrary wavelength.
/// </summary>
public interface IDispersionModel
{
    /// <summary>
    /// Returns the effective refractive index at the given wavelength.
    /// </summary>
    /// <param name="wavelengthNm">Wavelength in nanometers.</param>
    double NEffAt(double wavelengthNm);

    /// <summary>
    /// Returns the group index at the given wavelength.
    /// </summary>
    /// <param name="wavelengthNm">Wavelength in nanometers.</param>
    double GroupIndexAt(double wavelengthNm);

    /// <summary>
    /// Returns the propagation loss in dB/cm at the given wavelength.
    /// </summary>
    /// <param name="wavelengthNm">Wavelength in nanometers.</param>
    double LossDbPerCmAt(double wavelengthNm);
}
