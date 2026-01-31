using System.Text.Json.Serialization;

namespace CAP_DataAccess.Components.ComponentDraftMapper.DTOs
{
    /// <summary>
    /// DTO for a Process Design Kit (PDK) file containing multiple component definitions.
    /// This serves as the intermediate JSON format for foundry component libraries.
    /// </summary>
    public class PdkDraft
    {
        /// <summary>
        /// File format version for backwards compatibility.
        /// </summary>
        [JsonPropertyName("fileFormatVersion")]
        public int FileFormatVersion { get; set; } = 1;

        /// <summary>
        /// Name of the PDK (e.g., "AMF Si Photonics", "IMEC iSiPP50G").
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Optional description of the PDK.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Foundry or provider name.
        /// </summary>
        [JsonPropertyName("foundry")]
        public string? Foundry { get; set; }

        /// <summary>
        /// PDK version string.
        /// </summary>
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        /// <summary>
        /// Default wavelength in nm for this PDK (e.g., 1550 for C-band).
        /// </summary>
        [JsonPropertyName("defaultWavelengthNm")]
        public int DefaultWavelengthNm { get; set; } = 1550;

        /// <summary>
        /// Python module name for Nazca import (e.g., "amf", "imec").
        /// Used to generate: import {nazcaModuleName} as pdk
        /// </summary>
        [JsonPropertyName("nazcaModuleName")]
        public string? NazcaModuleName { get; set; }

        /// <summary>
        /// List of component definitions in this PDK.
        /// </summary>
        [JsonPropertyName("components")]
        public List<PdkComponentDraft> Components { get; set; } = new();
    }

    /// <summary>
    /// A component definition within a PDK.
    /// Simplified version of ComponentDraft optimized for PDK use.
    /// </summary>
    public class PdkComponentDraft
    {
        /// <summary>
        /// Display name of the component (e.g., "MMI 2x2 Coupler").
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Category for UI grouping (e.g., "Couplers", "Modulators", "I/O").
        /// </summary>
        [JsonPropertyName("category")]
        public string Category { get; set; } = "General";

        /// <summary>
        /// Nazca function name to call (e.g., "pdk.mmi2x2").
        /// </summary>
        [JsonPropertyName("nazcaFunction")]
        public string NazcaFunction { get; set; }

        /// <summary>
        /// Optional Nazca function parameters as string (e.g., "length=50, width=5").
        /// </summary>
        [JsonPropertyName("nazcaParameters")]
        public string? NazcaParameters { get; set; }

        /// <summary>
        /// Physical width in micrometers.
        /// </summary>
        [JsonPropertyName("widthMicrometers")]
        public double WidthMicrometers { get; set; }

        /// <summary>
        /// Physical height in micrometers.
        /// </summary>
        [JsonPropertyName("heightMicrometers")]
        public double HeightMicrometers { get; set; }

        /// <summary>
        /// Physical pin definitions with µm coordinates.
        /// </summary>
        [JsonPropertyName("pins")]
        public List<PhysicalPinDraft> Pins { get; set; } = new();

        /// <summary>
        /// Optional S-Matrix data for light simulation.
        /// When not provided, component acts as a black box (no light simulation).
        /// </summary>
        [JsonPropertyName("sMatrix")]
        public PdkSMatrixDraft? SMatrix { get; set; }

        /// <summary>
        /// Optional slider parameters (e.g., for phase shifters).
        /// </summary>
        [JsonPropertyName("sliders")]
        public List<SliderDraft>? Sliders { get; set; }
    }

    /// <summary>
    /// Simplified S-Matrix definition for PDK components.
    /// </summary>
    public class PdkSMatrixDraft
    {
        /// <summary>
        /// Wavelength in nm this S-Matrix applies to.
        /// </summary>
        [JsonPropertyName("wavelengthNm")]
        public int WavelengthNm { get; set; } = 1550;

        /// <summary>
        /// S-Matrix connections as list of (fromPin, toPin, magnitude, phaseDegrees).
        /// Magnitude is transmission amplitude (0-1), phase in degrees.
        /// </summary>
        [JsonPropertyName("connections")]
        public List<SMatrixConnection> Connections { get; set; } = new();
    }

    /// <summary>
    /// A single S-Matrix connection entry.
    /// </summary>
    public class SMatrixConnection
    {
        [JsonPropertyName("fromPin")]
        public string FromPin { get; set; }

        [JsonPropertyName("toPin")]
        public string ToPin { get; set; }

        /// <summary>
        /// Transmission amplitude (0-1). For a 50/50 splitter, use ~0.707 (sqrt(0.5)).
        /// </summary>
        [JsonPropertyName("magnitude")]
        public double Magnitude { get; set; }

        /// <summary>
        /// Phase shift in degrees.
        /// </summary>
        [JsonPropertyName("phaseDegrees")]
        public double PhaseDegrees { get; set; }
    }
}
