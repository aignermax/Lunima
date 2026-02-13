using System.Text.Json.Serialization;

namespace CAP_Core.LightCalculation.Validation
{
    /// <summary>
    /// Data transfer object for a single validation entry in JSON export.
    /// </summary>
    public class ValidationEntryDto
    {
        /// <summary>
        /// The severity level ("Warning" or "Error").
        /// </summary>
        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "";

        /// <summary>
        /// A human-readable description of the finding.
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = "";
    }
}
