using System.Text.Json;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.ComponentDraftMapper
{
    /// <summary>
    /// Serializes and writes a <see cref="PdkDraft"/> back to a JSON file.
    /// Used by the PDK Offset Editor to persist corrected NazcaOriginOffset values.
    /// </summary>
    public class PdkJsonSaver
    {
        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Writes a <see cref="PdkDraft"/> to the specified file path as formatted JSON.
        /// Overwrites the file if it already exists.
        /// </summary>
        /// <param name="pdk">The PDK draft to serialize.</param>
        /// <param name="filePath">Destination file path.</param>
        /// <exception cref="InvalidOperationException">Thrown when the file path is invalid or the directory does not exist.</exception>
        public void SaveToFile(PdkDraft pdk, string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                throw new InvalidOperationException($"Directory does not exist: {directory}");
            }

            var json = JsonSerializer.Serialize(pdk, WriteOptions);
            File.WriteAllText(filePath, json);
        }
    }
}
