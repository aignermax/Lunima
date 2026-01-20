using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CAP_DataAccess.Components.ComponentDraftMapper.DTOs
{
    public class ComponentDraft
    {
        [JsonPropertyName("fileFormatVersion")]
        public int FileFormatVersion { get; set; }
        [JsonPropertyName("identifier")]
        public string Identifier { get; set; }
        [JsonPropertyName("nazcaFunctionParameters")]
        public string NazcaFunctionParameters { get; set; }
        [JsonPropertyName("nazcaFunctionName")]
        public string NazcaFunctionName { get; set; }
        [JsonPropertyName("sceneResPath")]
        public string SceneResPath { get; set; }
        [JsonPropertyName("widthInTiles")]
        public int WidthInTiles { get; set; }
        [JsonPropertyName("heightInTiles")]
        public int HeightInTiles { get; set; }

        /// <summary>
        /// Physical width of the component in micrometers.
        /// Optional - when not provided, defaults to WidthInTiles * 250 µm.
        /// </summary>
        [JsonPropertyName("widthMicrometers")]
        public double? WidthMicrometers { get; set; }

        /// <summary>
        /// Physical height of the component in micrometers.
        /// Optional - when not provided, defaults to HeightInTiles * 250 µm.
        /// </summary>
        [JsonPropertyName("heightMicrometers")]
        public double? HeightMicrometers { get; set; }

        [JsonPropertyName("pins")]
        public List<PinDraft> Pins { get; set; }
        [JsonPropertyName("sMatrices")]
        public List<WaveLengthSpecificSMatrix> SMatrices { get; set; }
        [JsonPropertyName("overlays")]
        public List<Overlay> Overlays { get; set; }
        [JsonPropertyName("sliders")]
        public List<SliderDraft>? Sliders { get; set; }

        /// <summary>
        /// Physical pin definitions with µm coordinates for direct waveguide connections.
        /// Optional - when not provided, physical pins can be auto-generated from logical pins.
        /// </summary>
        [JsonPropertyName("physicalPins")]
        public List<PhysicalPinDraft>? PhysicalPins { get; set; }
    }
}
