using System.Text.Json.Serialization;

namespace CAP_DataAccess.Components.ComponentDraftMapper.DTOs
{
    /// <summary>
    /// Optional <c>bendLoss</c> block of an optical process cross-section: the coefficients of
    /// the radius-dependent bend-loss model (see <c>docs/BEND-LOSS-MODEL.md</c>). Every field is
    /// optional — omitted fields fall back to the defaults of <see cref="Family"/>, or of the
    /// family inferred from the process materials when no family is named. All fields are
    /// nullable and skipped when null, so PDKs without the block round-trip byte-identically.
    /// </summary>
    public class ProcessBendLossDraft
    {
        /// <summary>Waveguide family preset ("SoiStrip", "SiliconNitride", "InpDeepEtch", "InpShallowRib", "Generic").</summary>
        [JsonPropertyName("family")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Family { get; set; }

        /// <summary>Radiation loss in dB per 90° of turn at <see cref="RadiationReferenceRadiusUm"/>.</summary>
        [JsonPropertyName("radiationLossDbPer90")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? RadiationLossDbPer90 { get; set; }

        /// <summary>Radius (µm) the radiation loss is quoted at.</summary>
        [JsonPropertyName("radiationReferenceRadiusUm")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? RadiationReferenceRadiusUm { get; set; }

        /// <summary>Radius increase (µm) that reduces the radiation loss by a factor e.</summary>
        [JsonPropertyName("radiationDecayLengthUm")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? RadiationDecayLengthUm { get; set; }

        /// <summary>Smallest radius (µm) the coefficients are valid for; steeper extrapolation below.</summary>
        [JsonPropertyName("calibratedMinRadiusUm")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? CalibratedMinRadiusUm { get; set; }

        /// <summary>Loss (dB) of one straight↔bend junction at <see cref="JunctionReferenceRadiusUm"/>.</summary>
        [JsonPropertyName("junctionLossDb")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? JunctionLossDb { get; set; }

        /// <summary>Bend radius (µm) the junction loss is quoted at.</summary>
        [JsonPropertyName("junctionReferenceRadiusUm")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? JunctionReferenceRadiusUm { get; set; }

        /// <summary>Provenance of the coefficients (measurement, foundry document, paper).</summary>
        [JsonPropertyName("source")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Source { get; set; }

        /// <summary>Independent copy (all members are values or immutable strings).</summary>
        public ProcessBendLossDraft Clone() => (ProcessBendLossDraft)MemberwiseClone();
    }
}
