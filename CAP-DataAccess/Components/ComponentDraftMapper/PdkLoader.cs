using System.Text.Json;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.ComponentDraftMapper
{
    /// <summary>
    /// Loads PDK (Process Design Kit) JSON files containing component libraries.
    /// </summary>
    public class PdkLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>
        /// Loads a PDK from a JSON file.
        /// </summary>
        public PdkDraft LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"PDK file not found: {filePath}");
            }

            var json = File.ReadAllText(filePath);
            return LoadFromJson(json);
        }

        /// <summary>
        /// Loads a PDK from a JSON string.
        /// </summary>
        public PdkDraft LoadFromJson(string json)
        {
            var pdk = JsonSerializer.Deserialize<PdkDraft>(json, JsonOptions);
            if (pdk == null)
            {
                throw new InvalidOperationException("Failed to deserialize PDK JSON");
            }

            ValidatePdk(pdk);
            return pdk;
        }

        /// <summary>
        /// Loads all PDK files from a directory.
        /// </summary>
        public List<PdkDraft> LoadFromDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                throw new DirectoryNotFoundException($"PDK directory not found: {directoryPath}");
            }

            var pdks = new List<PdkDraft>();
            var jsonFiles = Directory.GetFiles(directoryPath, "*.json");

            foreach (var file in jsonFiles)
            {
                try
                {
                    var pdk = LoadFromFile(file);
                    pdks.Add(pdk);
                }
                catch (Exception ex)
                {
                    // Log warning but continue loading other PDKs
                    Console.WriteLine($"Warning: Failed to load PDK from {file}: {ex.Message}");
                }
            }

            return pdks;
        }

        private void ValidatePdk(PdkDraft pdk)
        {
            if (string.IsNullOrWhiteSpace(pdk.Name))
            {
                throw new InvalidOperationException("PDK must have a name");
            }

            foreach (var comp in pdk.Components)
            {
                ValidateComponent(comp, pdk.Name);
            }
        }

        private void ValidateComponent(PdkComponentDraft comp, string pdkName)
        {
            if (string.IsNullOrWhiteSpace(comp.Name))
            {
                throw new InvalidOperationException($"Component in PDK '{pdkName}' must have a name");
            }

            if (comp.WidthMicrometers <= 0)
            {
                throw new InvalidOperationException($"Component '{comp.Name}' must have positive width");
            }

            if (comp.HeightMicrometers <= 0)
            {
                throw new InvalidOperationException($"Component '{comp.Name}' must have positive height");
            }

            if (comp.Pins == null || comp.Pins.Count == 0)
            {
                throw new InvalidOperationException($"Component '{comp.Name}' must have at least one pin");
            }

            // Validate pin positions are within component bounds (with some tolerance)
            foreach (var pin in comp.Pins)
            {
                if (string.IsNullOrWhiteSpace(pin.Name))
                {
                    throw new InvalidOperationException($"Pin in component '{comp.Name}' must have a name");
                }

                // Allow small tolerance for pins at edges
                const double tolerance = 1.0;
                if (pin.OffsetXMicrometers < -tolerance || pin.OffsetXMicrometers > comp.WidthMicrometers + tolerance)
                {
                    Console.WriteLine($"Warning: Pin '{pin.Name}' X position ({pin.OffsetXMicrometers}) may be outside component bounds");
                }
                if (pin.OffsetYMicrometers < -tolerance || pin.OffsetYMicrometers > comp.HeightMicrometers + tolerance)
                {
                    Console.WriteLine($"Warning: Pin '{pin.Name}' Y position ({pin.OffsetYMicrometers}) may be outside component bounds");
                }
            }

            // Validate parametric S-Matrix if present
            if (comp.SMatrix != null && ParametricSMatrixMapper.IsParametric(comp.SMatrix))
            {
                ParametricSMatrixMapper.Validate(comp.SMatrix, comp.Name, comp.Pins);
            }
        }
    }
}
