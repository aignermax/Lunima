using System.Text.Json.Serialization;

namespace CAP_Core.Export;

/// <summary>
/// Result returned by <see cref="PdkNazcaParserService.ParseAsync"/>.
/// Mirrors the JSON schema produced by <c>scripts/parse_pdk.py</c>.
/// </summary>
public class PdkParseResult
{
    /// <summary>File format version (should be 1).</summary>
    [JsonPropertyName("fileFormatVersion")]
    public int FileFormatVersion { get; set; } = 1;

    /// <summary>PDK name (e.g., Python module name or user-supplied name).</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Optional description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Foundry / provider name.</summary>
    [JsonPropertyName("foundry")]
    public string? Foundry { get; set; }

    /// <summary>PDK version string.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>Default wavelength in nm.</summary>
    [JsonPropertyName("defaultWavelengthNm")]
    public int DefaultWavelengthNm { get; set; } = 1550;

    /// <summary>Python module name used for Nazca import.</summary>
    [JsonPropertyName("nazcaModuleName")]
    public string? NazcaModuleName { get; set; }

    /// <summary>List of parsed component geometries.</summary>
    [JsonPropertyName("components")]
    public List<ParsedComponentGeometry> Components { get; set; } = new();
}

/// <summary>
/// Geometry data for a single PDK cell extracted from Nazca.
/// </summary>
public class ParsedComponentGeometry
{
    /// <summary>Cell / function name (e.g., "ebeam_y_1550").</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>UI category label.</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "Imported";

    /// <summary>Nazca Python function name.</summary>
    [JsonPropertyName("nazcaFunction")]
    public string NazcaFunction { get; set; } = "";

    /// <summary>Optional Nazca function parameters.</summary>
    [JsonPropertyName("nazcaParameters")]
    public string? NazcaParameters { get; set; }

    /// <summary>Component bounding-box width in µm.</summary>
    [JsonPropertyName("widthMicrometers")]
    public double WidthMicrometers { get; set; }

    /// <summary>Component bounding-box height in µm.</summary>
    [JsonPropertyName("heightMicrometers")]
    public double HeightMicrometers { get; set; }

    /// <summary>
    /// X offset of the Nazca cell origin within the editor bounding box (µm).
    /// Equals <c>-xmin</c> from the Nazca bounding box.
    /// </summary>
    [JsonPropertyName("nazcaOriginOffsetX")]
    public double NazcaOriginOffsetX { get; set; }

    /// <summary>
    /// Y offset of the Nazca cell origin within the editor bounding box (µm).
    /// Equals <c>ymax</c> from the Nazca bounding box (Y-flip).
    /// </summary>
    [JsonPropertyName("nazcaOriginOffsetY")]
    public double NazcaOriginOffsetY { get; set; }

    /// <summary>Pin definitions in editor coordinate space.</summary>
    [JsonPropertyName("pins")]
    public List<ParsedPinGeometry> Pins { get; set; } = new();
}

/// <summary>
/// Pin geometry in editor coordinate space (Y-down, origin at top-left of bounding box).
/// </summary>
public class ParsedPinGeometry
{
    /// <summary>Pin name (e.g., "a0", "b0").</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>X offset from left edge of bounding box (µm).</summary>
    [JsonPropertyName("offsetXMicrometers")]
    public double OffsetXMicrometers { get; set; }

    /// <summary>Y offset from top edge of bounding box (µm).</summary>
    [JsonPropertyName("offsetYMicrometers")]
    public double OffsetYMicrometers { get; set; }

    /// <summary>Pin angle in degrees (editor convention: 0=right, 180=left, 270=down).</summary>
    [JsonPropertyName("angleDegrees")]
    public double AngleDegrees { get; set; }
}
