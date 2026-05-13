using System.Text.Json.Serialization;

namespace CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

/// <summary>
/// JSON DTO for a material dispersion block inside a PDK JSON file.
/// Supports two model types:
/// <list type="bullet">
///   <item><c>"polynomial"</c> — closed-form Taylor expansion around a center wavelength.</item>
///   <item><c>"tabulated"</c>  — linear interpolation over measured data points.</item>
/// </list>
/// When absent from a PDK component or PDK root, behaviour falls back to a constant
/// loss equal to the waveguide's <c>PropagationLossDbPerCm</c> scalar.
/// </summary>
public class MaterialDispersionDraft
{
    /// <summary>
    /// Dispersion model type: <c>"polynomial"</c> or <c>"tabulated"</c>.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "polynomial";

    /// <summary>
    /// Center wavelength in nm for polynomial models (e.g. 1550).
    /// Ignored for tabulated models.
    /// </summary>
    [JsonPropertyName("centerWavelengthNm")]
    public double CenterWavelengthNm { get; set; } = 1550.0;

    /// <summary>
    /// Effective index polynomial coefficients.
    /// Applies to the <c>"polynomial"</c> type only.
    /// </summary>
    [JsonPropertyName("effectiveIndex")]
    public EffectiveIndexDraft? EffectiveIndex { get; set; }

    /// <summary>
    /// Group index definition (polynomial constant or tabulated).
    /// Applies to the <c>"polynomial"</c> type only.
    /// </summary>
    [JsonPropertyName("groupIndex")]
    public GroupIndexDraft? GroupIndex { get; set; }

    /// <summary>
    /// Propagation loss definition — either a polynomial or tabulated model.
    /// </summary>
    [JsonPropertyName("propagationLossDbPerCm")]
    public LossDraft? PropagationLossDbPerCm { get; set; }
}

/// <summary>
/// Polynomial coefficients for the effective refractive index:
///   n_eff(λ) = n0 + n1·(λ - λ₀) + n2·(λ - λ₀)²
/// </summary>
public class EffectiveIndexDraft
{
    /// <summary>n_eff at the center wavelength (zeroth-order).</summary>
    [JsonPropertyName("n0")]
    public double N0 { get; set; } = 2.45;

    /// <summary>First-order dispersion coefficient [RIU/nm].</summary>
    [JsonPropertyName("n1")]
    public double N1 { get; set; } = 0.0;

    /// <summary>Second-order dispersion coefficient [RIU/nm²].</summary>
    [JsonPropertyName("n2")]
    public double N2 { get; set; } = 0.0;
}

/// <summary>
/// Group index at the center wavelength (constant across the band).
/// </summary>
public class GroupIndexDraft
{
    /// <summary>Group index n_g at the center wavelength.</summary>
    [JsonPropertyName("ng0")]
    public double Ng0 { get; set; } = 4.2;
}

/// <summary>
/// Propagation loss definition — scalar, polynomial, or tabulated.
/// </summary>
public class LossDraft
{
    /// <summary>
    /// Loss model type: <c>"constant"</c> (default) or <c>"tabulated"</c>.
    /// When <c>"constant"</c>, use <see cref="ConstantDbPerCm"/>.
    /// When <c>"tabulated"</c>, use <see cref="Points"/>.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "constant";

    /// <summary>Constant loss value in dB/cm. Used when <see cref="Type"/> is <c>"constant"</c>.</summary>
    [JsonPropertyName("constantDbPerCm")]
    public double ConstantDbPerCm { get; set; } = 0.5;

    /// <summary>
    /// Tabulated loss points as [[wavelengthNm, lossDbPerCm], ...].
    /// Used when <see cref="Type"/> is <c>"tabulated"</c>.
    /// </summary>
    [JsonPropertyName("points")]
    public List<List<double>>? Points { get; set; }
}
