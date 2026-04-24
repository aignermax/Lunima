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
        /// The write is atomic: content is first written to a sibling <c>.tmp</c>
        /// file and then renamed, so a crash mid-write leaves the original PDK
        /// JSON intact (this file is the source of truth for GDS export).
        /// </summary>
        /// <param name="pdk">The PDK draft to serialize.</param>
        /// <param name="filePath">Destination file path.</param>
        /// <exception cref="ArgumentNullException"><paramref name="pdk"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="filePath"/> is null or empty.</exception>
        /// <exception cref="InvalidOperationException">The destination directory does not exist.</exception>
        public void SaveToFile(PdkDraft pdk, string filePath)
        {
            ArgumentNullException.ThrowIfNull(pdk);
            ArgumentException.ThrowIfNullOrEmpty(filePath);

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                throw new InvalidOperationException($"Directory does not exist: {directory}");
            }

            var json = JsonSerializer.Serialize(pdk, WriteOptions);
            var tempPath = filePath + ".tmp";
            var moved = false;
            try
            {
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, filePath, overwrite: true);
                moved = true;
            }
            finally
            {
                if (!moved && File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch
                    {
                        // Best-effort cleanup only — the caller sees the real
                        // save failure, and a leftover <path>.tmp is overwritten
                        // on the next successful save anyway.
                    }
                }
            }
        }
    }
}
