using System.Text.Json.Serialization;

namespace CAP_DataAccess.Components.ComponentDraftMapper.DTOs
{
    /// <summary>
    /// DTO for a named parameter in a parametric S-Matrix definition.
    /// Parameters are referenced by name in formula expressions.
    /// </summary>
    public class ParameterDefinitionDraft
    {
        /// <summary>
        /// Name of the parameter (e.g., "coupling_ratio", "phase_shift").
        /// Used as the variable name in formulas.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>
        /// Default value for the parameter.
        /// </summary>
        [JsonPropertyName("defaultValue")]
        public double DefaultValue { get; set; }

        /// <summary>
        /// Minimum allowed value.
        /// </summary>
        [JsonPropertyName("minValue")]
        public double MinValue { get; set; }

        /// <summary>
        /// Maximum allowed value.
        /// </summary>
        [JsonPropertyName("maxValue")]
        public double MaxValue { get; set; } = 1.0;

        /// <summary>
        /// Optional display label for UI sliders.
        /// </summary>
        [JsonPropertyName("label")]
        public string? Label { get; set; }

        /// <summary>
        /// Slider number (0-based) that controls this parameter.
        /// -1 means this parameter is not linked to a slider.
        /// </summary>
        [JsonPropertyName("sliderNumber")]
        public int SliderNumber { get; set; } = -1;
    }
}
