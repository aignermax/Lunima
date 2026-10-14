using System.Text.Json.Serialization;

namespace CAP_Core.Components.Core;

public partial class Component
{
    /// <summary>
    /// Edge couplers: facet mode-field diameter (µm) from the PDK, re-applied from the
    /// template on load; null = default mode. A die-to-die link between couplers of
    /// different spot sizes loses light to the mode mismatch.
    /// </summary>
    [JsonIgnore]
    public double? ModeFieldDiameterMicrometers { get; set; }
}
