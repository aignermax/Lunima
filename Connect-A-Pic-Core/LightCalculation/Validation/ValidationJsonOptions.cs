using System.Text.Json;

namespace CAP_Core.LightCalculation.Validation
{
    /// <summary>
    /// Shared JSON serializer options for validation report export.
    /// </summary>
    public static class ValidationJsonOptions
    {
        /// <summary>
        /// Default options: indented, camelCase property names.
        /// </summary>
        public static readonly JsonSerializerOptions Default = new()
        {
            WriteIndented = true,
        };
    }
}
