using System.Text.Json.Serialization;

namespace CAP_Core.Components.Core;

public partial class Component
{
    /// <summary>
    /// Wavelength-dependent waveguide dispersion of the PDK this instance was created
    /// from (the draft's <c>materialDispersion</c>, resolved to a domain model).
    /// Stamped from the template at placement and on load; routed waveguide
    /// connections touching this component inherit it as their
    /// <see cref="CAP_Core.Components.Connections.WaveguideConnection.DispersionModel"/>
    /// so coherent propagation uses the PDK's n_eff(λ)/n_g(λ) instead of the global
    /// fallback. Null when the PDK declares no dispersion block — the connection then
    /// keeps the documented fallback behaviour. Runtime-only: persistence flows
    /// through the template on load, like <see cref="TemplateName"/>.
    /// </summary>
    [JsonIgnore]
    public CAP_Core.LightCalculation.MaterialDispersion.IDispersionModel? WaveguideDispersion { get; set; }
}
