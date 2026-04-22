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

        /// <summary>
        /// Updates a single component's NazcaOriginOffset in the given PDK and writes the file.
        /// Returns false if the component is not found in the PDK.
        /// </summary>
        /// <param name="pdk">The PDK draft to update.</param>
        /// <param name="componentName">Name of the component to update.</param>
        /// <param name="offsetX">New X offset in micrometers.</param>
        /// <param name="offsetY">New Y offset in micrometers.</param>
        /// <param name="filePath">Destination file path.</param>
        /// <returns>True when the component was found and saved; false if not found.</returns>
        public bool UpdateComponentOffset(
            PdkDraft pdk,
            string componentName,
            double offsetX,
            double offsetY,
            string filePath)
        {
            var comp = pdk.Components.FirstOrDefault(c =>
                string.Equals(c.Name, componentName, StringComparison.OrdinalIgnoreCase));

            if (comp == null)
                return false;

            comp.NazcaOriginOffsetX = offsetX;
            comp.NazcaOriginOffsetY = offsetY;

            SaveToFile(pdk, filePath);
            return true;
        }
    }
}
