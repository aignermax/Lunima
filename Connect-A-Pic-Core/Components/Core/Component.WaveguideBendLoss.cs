using System.Text.Json.Serialization;
using CAP_Core.LightCalculation.BendLoss;

namespace CAP_Core.Components.Core;

public partial class Component
{
    /// <summary>
    /// Radius-dependent bend-loss model of the waveguide cross-section of the PDK process this
    /// instance was created from. Stamped from the template at placement and on load; routed
    /// connections touching this component inherit it as their
    /// <see cref="CAP_Core.Components.Connections.WaveguideConnection.BendLoss"/>, so a SiN
    /// chiplet's routes lose light at radii where SOI routes would not. Null when the PDK
    /// declares no process — the connection then uses <see cref="BendLossPresets.Generic"/>.
    /// Runtime-only: persistence flows through the template on load, like
    /// <see cref="WaveguideDispersion"/>.
    /// </summary>
    [JsonIgnore]
    public BendLossModel? WaveguideBendLoss { get; set; }
}
