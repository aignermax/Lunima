namespace CAP_Core.LightCalculation.Validation
{
    /// <summary>
    /// Exports validation results to JSON files.
    /// </summary>
    public class ValidationReportExporter
    {
        /// <summary>
        /// Exports a validation result to a JSON file at the given path.
        /// </summary>
        /// <param name="result">The validation result to export.</param>
        /// <param name="filePath">The file path to write to.</param>
        /// <param name="wavelengthNm">Optional wavelength in nm.</param>
        public async Task ExportAsync(
            SMatrixValidationResult result,
            string filePath,
            int? wavelengthNm = null)
        {
            var json = result.ToJson(wavelengthNm);
            await File.WriteAllTextAsync(filePath, json);
        }

        /// <summary>
        /// Generates an auto-export file path next to the given design file.
        /// </summary>
        /// <param name="designFilePath">Path to the design file.</param>
        /// <returns>A path with .validation.json extension.</returns>
        public static string GetAutoExportPath(string designFilePath)
        {
            var directory = Path.GetDirectoryName(designFilePath) ?? ".";
            var baseName = Path.GetFileNameWithoutExtension(designFilePath);
            return Path.Combine(directory, $"{baseName}.validation.json");
        }
    }
}
