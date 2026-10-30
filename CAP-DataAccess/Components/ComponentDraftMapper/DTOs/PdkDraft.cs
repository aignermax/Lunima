using System.Text.Json.Serialization;

namespace CAP_DataAccess.Components.ComponentDraftMapper.DTOs
{
    public class PdkDraft
    {
        [JsonPropertyName("fileFormatVersion")]
        public int FileFormatVersion { get; set; } = 1;

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("foundry")]
        public string? Foundry { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("defaultWavelengthNm")]
        public int DefaultWavelengthNm { get; set; } = 1550;

        [JsonPropertyName("processAgnostic")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool ProcessAgnostic { get; set; }

        [JsonPropertyName("nazcaModuleName")]
        public string? NazcaModuleName { get; set; }

        [JsonPropertyName("materialDispersion")]
        public MaterialDispersionDraft? MaterialDispersion { get; set; }

        [JsonPropertyName("process")]
        public ProcessDefinition? Process { get; set; }

        [JsonPropertyName("backend")]
        public string? Backend { get; set; }

        [JsonPropertyName("gdsFactoryRoutingCrossSection")]
        public string? GdsFactoryRoutingCrossSection { get; set; }

        [JsonIgnore]
        public bool IsGdsFactoryBackend =>
            string.Equals(Backend, "gdsfactory", System.StringComparison.OrdinalIgnoreCase);

        [JsonPropertyName("components")]
        public List<PdkComponentDraft> Components { get; set; } = new();

        [JsonIgnore]
        public string? FilePath { get; set; }
    }

    public class PdkComponentDraft
    {
        /// <summary>Captures an independent component definition for background work.</summary>
        public PdkComponentDraft Copy()
        {
            var copy = (PdkComponentDraft)MemberwiseClone();
            copy.OutlinePolygons = OutlinePolygons?.Select(polygon => polygon.Copy()).ToList();
            copy.Pins = Pins.Select(pin => pin.Copy()).ToList();
            copy.SMatrix = SMatrix?.Copy();
            copy.MaterialDispersion = MaterialDispersion?.Copy();
            copy.Sliders = Sliders?.Select(slider => slider.Copy()).ToList();
            copy.CompactModelParameters = CompactModelParameters is null ? null
                : new(CompactModelParameters, CompactModelParameters.Comparer);
            return copy;
        }

        /// <summary>Original embedded GDS cell for expanded template variants.</summary>
        [JsonPropertyName("importedSourceCellName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ImportedSourceCellName { get; set; }

        /// <summary>True for the own polygons of a source cell, excluding its references.</summary>
        [JsonPropertyName("importedOwnGeometryOnly")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool ImportedOwnGeometryOnly { get; set; }

        /// <summary>Explicit imported model readiness; null preserves legacy component behavior.</summary>
        [JsonPropertyName("importedModelStatus")]
        public string? ImportedModelStatus { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; } = "General";

        [JsonPropertyName("nazcaFunction")]
        public string NazcaFunction { get; set; }

        [JsonPropertyName("gdsFactoryFunction")]
        public string? GdsFactoryFunction { get; set; }

        [JsonPropertyName("nazcaParameters")]
        public string? NazcaParameters { get; set; }

        [JsonPropertyName("widthMicrometers")]
        public double WidthMicrometers { get; set; }

        [JsonPropertyName("heightMicrometers")]
        public double HeightMicrometers { get; set; }

        // Kept next to the bbox dimensions (not at class end) so the saver emits these
        // in the original hand-written JSON order and does not churn the file on save.
        [JsonPropertyName("nazcaOriginOffsetX")]
        public double? NazcaOriginOffsetX { get; set; }

        [JsonPropertyName("nazcaOriginOffsetY")]
        public double? NazcaOriginOffsetY { get; set; }

        /// <summary>
        /// Imported GDS outline polygons (µm, Y-down, relative to the component
        /// bbox top-left). Optional: absent in all hand-written PDKs, so null must
        /// stay valid — the canvas falls back to rectangle rendering then.
        /// <para>
        /// Contract: the list is treated as immutable after construction. The
        /// preview renderer caches built geometry by LIST IDENTITY, so mutating
        /// an instance in place would leave stale geometry on screen — always
        /// assign a new list instead of editing an existing one.
        /// </para>
        /// </summary>
        [JsonPropertyName("outlinePolygons")]
        public List<CAP_Core.Components.Core.OutlinePolygon>? OutlinePolygons { get; set; }

        [JsonPropertyName("pins")]
        public List<PhysicalPinDraft> Pins { get; set; } = new();

        /// <summary>
        /// Optional mode-field diameter (µm) at the facet of an edge coupler, used for the
        /// die-to-die coupling loss including spot-size mismatch. Omitted = default mode size.
        /// </summary>
        [JsonPropertyName("modeFieldDiameterMicrometers")]
        public double? ModeFieldDiameterMicrometers { get; set; }

        [JsonPropertyName("sMatrix")]
        public PdkSMatrixDraft? SMatrix { get; set; }

        [JsonPropertyName("materialDispersion")]
        public MaterialDispersionDraft? MaterialDispersion { get; set; }

        [JsonPropertyName("sliders")]
        public List<SliderDraft>? Sliders { get; set; }

        [JsonPropertyName("compactModel")]
        public string? CompactModel { get; set; }

        [JsonPropertyName("compactModelParameters")]
        public Dictionary<string, double>? CompactModelParameters { get; set; }

        [JsonPropertyName("rawCode")]
        public string? RawCode { get; set; }

        [JsonPropertyName("rawCodeBackend")]
        public string? RawCodeBackend { get; set; }
    }

    public class PdkSMatrixDraft
    {
        /// <summary>Copies all wavelength, connection and parameter values.</summary>
        public PdkSMatrixDraft Copy()
        {
            var copy = (PdkSMatrixDraft)MemberwiseClone();
            copy.Connections = Connections.Select(connection => connection.Copy()).ToList();
            copy.WavelengthData = WavelengthData?.Select(entry => entry.Copy()).ToList();
            copy.Parameters = Parameters?.Select(parameter => parameter.Copy()).ToList();
            return copy;
        }

        [JsonPropertyName("wavelengthNm")]
        public int WavelengthNm { get; set; } = 1550;

        [JsonPropertyName("connections")]
        public List<SMatrixConnection> Connections { get; set; } = new();

        [JsonPropertyName("wavelengthData")]
        public List<WavelengthSMatrixEntry>? WavelengthData { get; set; }

        [JsonPropertyName("parameters")]
        public List<ParameterDefinitionDraft>? Parameters { get; set; }

        // Provenance of a user-computed/imported matrix (e.g. "FDTD Tidy3D Cloud 2D").
        // Absent in older files = bundled/PDK original.
        [JsonPropertyName("sourceNote")]
        public string? SourceNote { get; set; }

        [JsonPropertyName("sourceTimestampUtc")]
        public string? SourceTimestampUtc { get; set; }
    }

    public class WavelengthSMatrixEntry
    {
        /// <summary>Copies the connections at this wavelength.</summary>
        public WavelengthSMatrixEntry Copy() => new()
        {
            WavelengthNm = WavelengthNm,
            Connections = Connections.Select(connection => connection.Copy()).ToList()
        };

        [JsonPropertyName("wavelengthNm")]
        public int WavelengthNm { get; set; }

        [JsonPropertyName("connections")]
        public List<SMatrixConnection> Connections { get; set; } = new();
    }

    public class SMatrixConnection
    {
        /// <summary>Copies the scalar definition for an independent snapshot.</summary>
        public SMatrixConnection Copy() => (SMatrixConnection)MemberwiseClone();

        [JsonPropertyName("fromPin")]
        public string FromPin { get; set; }

        [JsonPropertyName("toPin")]
        public string ToPin { get; set; }

        [JsonPropertyName("magnitude")]
        public double Magnitude { get; set; }

        [JsonPropertyName("phaseDegrees")]
        public double PhaseDegrees { get; set; }

        [JsonPropertyName("magnitudeFormula")]
        public string? MagnitudeFormula { get; set; }

        [JsonPropertyName("phaseDegreesFormula")]
        public string? PhaseDegreesFormula { get; set; }

        [JsonIgnore]
        public bool IsParametric =>
            !string.IsNullOrWhiteSpace(MagnitudeFormula) ||
            !string.IsNullOrWhiteSpace(PhaseDegreesFormula);
    }
}
