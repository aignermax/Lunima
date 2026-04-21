using System.Text.Json;
using CAP_DataAccess.Persistence.PIR;
using CAP.Avalonia.ViewModels;
using Shouldly;
using Xunit;

namespace UnitTests.Persistence;

/// <summary>
/// Tests for PIR (Photonic Intermediate Representation) data model serialization.
/// Verifies that the .lun v2.0 format can store and restore all new PIR sections.
/// Legacy v1 files are rejected at load time (see FileOperationsViewModel); the
/// DTOs themselves remain tolerant during raw JSON deserialization.
/// </summary>
public class PirDataSerializationTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // ─── FormatVersion ───────────────────────────────────────────────────────

    [Fact]
    public void DesignFileData_DefaultFormatVersion_IsNull()
    {
        var data = new DesignFileData();
        data.FormatVersion.ShouldBeNull();
    }

    [Fact]
    public void DesignFileData_Roundtrip_PreservesFormatVersion()
    {
        var data = new DesignFileData { FormatVersion = "2.0" };
        var json = JsonSerializer.Serialize(data, SerializerOptions);
        var loaded = JsonSerializer.Deserialize<DesignFileData>(json)!;

        loaded.FormatVersion.ShouldBe("2.0");
    }

    // ─── S-Matrix Serialization ───────────────────────────────────────────────

    [Fact]
    public void SMatrixData_Roundtrip_PreservesMatrixValues()
    {
        var entry = new SMatrixWavelengthEntry
        {
            Rows = 2,
            Cols = 2,
            Real = new List<double> { 0.0, 0.9, 0.9, 0.0 },
            Imag = new List<double> { 0.0, 0.1, 0.1, 0.0 },
            PortNames = new List<string> { "in1", "out1" }
        };
        var compData = new ComponentSMatrixData
        {
            SourceNote = "Test PDK",
            Wavelengths = new Dictionary<string, SMatrixWavelengthEntry>
            {
                ["1550"] = entry
            }
        };
        var designData = new DesignFileData
        {
            FormatVersion = "2.0",
            SMatrices = new Dictionary<string, ComponentSMatrixData>
            {
                ["MMI_1x2_1"] = compData
            }
        };

        var json = JsonSerializer.Serialize(designData, SerializerOptions);
        var loaded = JsonSerializer.Deserialize<DesignFileData>(json)!;

        loaded.SMatrices.ShouldNotBeNull();
        loaded.SMatrices!.ContainsKey("MMI_1x2_1").ShouldBeTrue();
        var loadedComp = loaded.SMatrices["MMI_1x2_1"];
        loadedComp.SourceNote.ShouldBe("Test PDK");
        loadedComp.Wavelengths.ContainsKey("1550").ShouldBeTrue();
        var loadedEntry = loadedComp.Wavelengths["1550"];
        loadedEntry.Rows.ShouldBe(2);
        loadedEntry.Cols.ShouldBe(2);
        loadedEntry.Real.ShouldBe(new List<double> { 0.0, 0.9, 0.9, 0.0 });
        loadedEntry.Imag.ShouldBe(new List<double> { 0.0, 0.1, 0.1, 0.0 });
        loadedEntry.PortNames.ShouldNotBeNull();
        loadedEntry.PortNames![0].ShouldBe("in1");
    }

    [Fact]
    public void SMatrixData_MultipleWavelengths_AllPreserved()
    {
        var compData = new ComponentSMatrixData
        {
            Wavelengths = new Dictionary<string, SMatrixWavelengthEntry>
            {
                ["1550"] = new SMatrixWavelengthEntry { Rows = 2, Cols = 2, Real = new List<double> { 0.1, 0.9, 0.9, 0.1 }, Imag = new List<double> { 0, 0, 0, 0 } },
                ["1310"] = new SMatrixWavelengthEntry { Rows = 2, Cols = 2, Real = new List<double> { 0.15, 0.85, 0.85, 0.15 }, Imag = new List<double> { 0, 0, 0, 0 } }
            }
        };

        var json = JsonSerializer.Serialize(compData, SerializerOptions);
        var loaded = JsonSerializer.Deserialize<ComponentSMatrixData>(json)!;

        loaded.Wavelengths.Count.ShouldBe(2);
        loaded.Wavelengths.ContainsKey("1550").ShouldBeTrue();
        loaded.Wavelengths.ContainsKey("1310").ShouldBeTrue();
    }

    // ─── Simulation Results ───────────────────────────────────────────────────

    [Fact]
    public void SimulationResultsData_Roundtrip_PreservesAllFields()
    {
        var results = new SimulationResultsData
        {
            LastRun = new SimulationRunData
            {
                Timestamp = "2024-01-15T10:30:00Z",
                WavelengthNm = 1550,
                PowerFlow = new Dictionary<string, ConnectionPowerFlowData>
                {
                    ["conn-guid-1"] = new ConnectionPowerFlowData
                    {
                        InputPower = 1.0,
                        OutputPower = 0.5,
                        NormalizedPowerDb = -3.01
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(results, SerializerOptions);
        var loaded = JsonSerializer.Deserialize<SimulationResultsData>(json)!;

        loaded.LastRun.ShouldNotBeNull();
        loaded.LastRun!.Timestamp.ShouldBe("2024-01-15T10:30:00Z");
        loaded.LastRun.WavelengthNm.ShouldBe(1550);
        loaded.LastRun.PowerFlow.ContainsKey("conn-guid-1").ShouldBeTrue();
        loaded.LastRun.PowerFlow["conn-guid-1"].NormalizedPowerDb.ShouldBe(-3.01);
    }

    [Fact]
    public void ParameterSweepResultData_Roundtrip_PreservesAllFields()
    {
        var sweep = new ParameterSweepResultData
        {
            ParameterName = "SliderValue",
            ComponentIdentifier = "MMI_1x2_1",
            ParameterValues = new List<double> { 0.0, 0.25, 0.5, 0.75, 1.0 },
            OutputPowers = new List<double> { 0.1, 0.3, 0.5, 0.7, 0.9 },
            TargetConnectionId = "conn-guid-2",
            WavelengthNm = 1310,
            Timestamp = "2024-02-01T08:00:00Z"
        };

        var json = JsonSerializer.Serialize(sweep, SerializerOptions);
        var loaded = JsonSerializer.Deserialize<ParameterSweepResultData>(json)!;

        loaded.ParameterName.ShouldBe("SliderValue");
        loaded.ComponentIdentifier.ShouldBe("MMI_1x2_1");
        loaded.ParameterValues.Count.ShouldBe(5);
        loaded.OutputPowers[2].ShouldBe(0.5);
        loaded.WavelengthNm.ShouldBe(1310);
    }

    // ─── Metadata ─────────────────────────────────────────────────────────────

    [Fact]
    public void DesignMetadata_Roundtrip_PreservesAllFields()
    {
        var metadata = new DesignMetadata
        {
            Description = "Test photonic chip design",
            PdkVersions = new Dictionary<string, string> { ["siepic-ebeam"] = "v1.2.0" },
            DesignRules = new DesignRulesData
            {
                MinBendRadiusMicrometers = 10.0,
                MinSpacingMicrometers = 5.0,
                WaveguideWidthMicrometers = 0.5
            },
            Authorship = new AuthorshipData
            {
                Created = "2024-01-01",
                Modified = "2024-01-15T10:30:00Z",
                Author = "TestUser",
                Version = "v1.0"
            }
        };

        var json = JsonSerializer.Serialize(metadata, SerializerOptions);
        var loaded = JsonSerializer.Deserialize<DesignMetadata>(json)!;

        loaded.Description.ShouldBe("Test photonic chip design");
        loaded.PdkVersions["siepic-ebeam"].ShouldBe("v1.2.0");
        loaded.DesignRules.ShouldNotBeNull();
        loaded.DesignRules!.MinBendRadiusMicrometers.ShouldBe(10.0);
        loaded.DesignRules.MinSpacingMicrometers.ShouldBe(5.0);
        loaded.Authorship.Created.ShouldBe("2024-01-01");
        loaded.Authorship.Modified.ShouldBe("2024-01-15T10:30:00Z");
        loaded.Authorship.Author.ShouldBe("TestUser");
    }

    [Fact]
    public void AuthorshipData_CreatedDate_PreservedOnRoundtrip()
    {
        // Created date must not change between saves
        const string originalCreated = "2024-01-01";
        var authorship = new AuthorshipData
        {
            Created = originalCreated,
            Modified = "2024-01-10T00:00:00Z"
        };

        var json = JsonSerializer.Serialize(authorship, SerializerOptions);
        var loaded = JsonSerializer.Deserialize<AuthorshipData>(json)!;

        loaded.Created.ShouldBe(originalCreated);
    }

    // ─── External References ──────────────────────────────────────────────────

    [Fact]
    public void ExternalReferenceData_Roundtrip_PreservesAllFields()
    {
        var extRef = new ExternalReferenceData
        {
            ComponentIdentifier = "mmi_2x2_custom",
            Tool = "tidy3d",
            FilePath = "simulations/mmi_sweep.json",
            FileHash = "sha256:abc123",
            Description = "FDTD simulation of custom MMI at 1550 nm",
            LastVerified = "2024-01-12T09:00:00Z"
        };

        var json = JsonSerializer.Serialize(extRef, SerializerOptions);
        var loaded = JsonSerializer.Deserialize<ExternalReferenceData>(json)!;

        loaded.ComponentIdentifier.ShouldBe("mmi_2x2_custom");
        loaded.Tool.ShouldBe("tidy3d");
        loaded.FilePath.ShouldBe("simulations/mmi_sweep.json");
        loaded.FileHash.ShouldBe("sha256:abc123");
        loaded.Description.ShouldBe("FDTD simulation of custom MMI at 1550 nm");
    }

    [Fact]
    public void DesignFileData_WithAllPirSections_FullRoundtrip()
    {
        var data = new DesignFileData
        {
            FormatVersion = "2.0",
            SMatrices = new Dictionary<string, ComponentSMatrixData>
            {
                ["comp1"] = new ComponentSMatrixData
                {
                    Wavelengths = new Dictionary<string, SMatrixWavelengthEntry>
                    {
                        ["1550"] = new SMatrixWavelengthEntry
                        {
                            Rows = 2, Cols = 2,
                            Real = new List<double> { 0, 1, 1, 0 },
                            Imag = new List<double> { 0, 0, 0, 0 }
                        }
                    }
                }
            },
            SimulationResults = new SimulationResultsData
            {
                LastRun = new SimulationRunData
                {
                    Timestamp = "2024-01-15T10:00:00Z",
                    WavelengthNm = 1550,
                    PowerFlow = new Dictionary<string, ConnectionPowerFlowData>()
                }
            },
            Metadata = new DesignMetadata
            {
                Authorship = new AuthorshipData { Created = "2024-01-01", Modified = "2024-01-15T10:00:00Z" }
            },
            ExternalReferences = new List<ExternalReferenceData>
            {
                new ExternalReferenceData { ComponentIdentifier = "comp1", Tool = "tidy3d", FilePath = "sim.json" }
            }
        };

        var json = JsonSerializer.Serialize(data, SerializerOptions);
        var loaded = JsonSerializer.Deserialize<DesignFileData>(json)!;

        loaded.FormatVersion.ShouldBe("2.0");
        loaded.SMatrices.ShouldNotBeNull();
        loaded.SMatrices!.Count.ShouldBe(1);
        loaded.SimulationResults.ShouldNotBeNull();
        loaded.SimulationResults!.LastRun!.WavelengthNm.ShouldBe(1550);
        loaded.Metadata.ShouldNotBeNull();
        loaded.Metadata!.Authorship.Created.ShouldBe("2024-01-01");
        loaded.ExternalReferences.ShouldNotBeNull();
        loaded.ExternalReferences!.Count.ShouldBe(1);
        loaded.ExternalReferences[0].Tool.ShouldBe("tidy3d");
    }
}
