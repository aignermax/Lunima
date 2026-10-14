using System.Text.Json.Serialization;
using CAP_Core.Routing.WaveguideCompanions;

namespace CAP_DataAccess.Components.ComponentDraftMapper.DTOs
{
    /// <summary>
    /// A process-defined companion layer drawn along every routed optical waveguide — a
    /// cladding opening, etch trench or keep-out band on its own GDS layer, the JSON form of
    /// a Nazca xsection layer with <c>growx</c> (<c>nd.add_layer2xsection(..., growx=…)</c>).
    /// Declare exactly one of <see cref="OffsetUm"/> (growth beyond each core edge) and
    /// <see cref="WidthUm"/> (absolute band width centered on the core).
    /// </summary>
    public class ProcessCompanionLayer
    {
        /// <summary>Band name (e.g. "ShallowTrench").</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>GDS layer number of the band.</summary>
        [JsonPropertyName("layer")]
        public int Layer { get; set; }

        /// <summary>GDS datatype of the band.</summary>
        [JsonPropertyName("datatype")]
        public int Datatype { get; set; }

        /// <summary>Foundry meaning ("Cladding" / "Trench" / "KeepOut"); only KeepOut affects DRC.</summary>
        [JsonPropertyName("role")]
        [JsonConverter(typeof(JsonStringEnumConverter<WaveguideCompanionRole>))]
        public WaveguideCompanionRole Role { get; set; } = WaveguideCompanionRole.Trench;

        /// <summary>Growth beyond each core edge in µm (Nazca <c>growx</c>).</summary>
        [JsonPropertyName("offsetUm")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? OffsetUm { get; set; }

        /// <summary>Absolute band width in µm, centered on the core.</summary>
        [JsonPropertyName("widthUm")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? WidthUm { get; set; }

        /// <summary>Human-readable description / provenance of the value.</summary>
        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; set; }
    }
}
