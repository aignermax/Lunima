using System.Text.Json.Serialization;

namespace CAP_Core.LightCalculation.Validation
{
    /// <summary>
    /// Data transfer object for the full validation report JSON export.
    /// </summary>
    public class ValidationReportDto
    {
        /// <summary>
        /// ISO 8601 timestamp of when the validation was performed.
        /// </summary>
        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = "";

        /// <summary>
        /// The laser wavelength in nanometers used during simulation.
        /// </summary>
        [JsonPropertyName("wavelengthNm")]
        public int? WavelengthNm { get; set; }

        /// <summary>
        /// Whether the validation passed with no errors.
        /// </summary>
        [JsonPropertyName("isValid")]
        public bool IsValid { get; set; }

        /// <summary>
        /// Whether the validation produced any warnings.
        /// </summary>
        [JsonPropertyName("hasWarnings")]
        public bool HasWarnings { get; set; }

        /// <summary>
        /// Total number of errors found.
        /// </summary>
        [JsonPropertyName("errorCount")]
        public int ErrorCount { get; set; }

        /// <summary>
        /// Total number of warnings found.
        /// </summary>
        [JsonPropertyName("warningCount")]
        public int WarningCount { get; set; }

        /// <summary>
        /// The list of error entries.
        /// </summary>
        [JsonPropertyName("errors")]
        public List<ValidationEntryDto> Errors { get; set; } = new();

        /// <summary>
        /// The list of warning entries.
        /// </summary>
        [JsonPropertyName("warnings")]
        public List<ValidationEntryDto> Warnings { get; set; } = new();
    }
}
