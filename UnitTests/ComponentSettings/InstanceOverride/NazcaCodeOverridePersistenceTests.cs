using System.Text.Json;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;
using Xunit;

namespace UnitTests.ComponentSettings.InstanceOverride;

/// <summary>
/// Tests that <see cref="NazcaCodeOverride"/> serialises / deserialises
/// correctly via System.Text.Json, matching what FileOperationsViewModel
/// writes to and reads from the .lun file.
/// </summary>
public class NazcaCodeOverridePersistenceTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void RoundTrip_AllFields_PreservesValues()
    {
        var original = new NazcaCodeOverride
        {
            FunctionName = "custom_mmi1x2",
            FunctionParameters = "width=3.5,length=10",
            ModuleName = "my_pdk",
            TemplateFunctionName = "ebeam_mmi1x2",
            TemplateFunctionParameters = "width=2.0,length=8",
            TemplateModuleName = "siepic"
        };

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<NazcaCodeOverride>(json, Options);

        restored.ShouldNotBeNull();
        restored!.FunctionName.ShouldBe("custom_mmi1x2");
        restored.FunctionParameters.ShouldBe("width=3.5,length=10");
        restored.ModuleName.ShouldBe("my_pdk");
        restored.TemplateFunctionName.ShouldBe("ebeam_mmi1x2");
        restored.TemplateFunctionParameters.ShouldBe("width=2.0,length=8");
        restored.TemplateModuleName.ShouldBe("siepic");
    }

    [Fact]
    public void RoundTrip_NullOptionalFields_OmittedFromJson()
    {
        var original = new NazcaCodeOverride
        {
            FunctionName = "custom_mmi1x2",
            FunctionParameters = "width=3.5",
            TemplateFunctionName = "ebeam_mmi1x2",
            TemplateFunctionParameters = "width=2.0"
            // ModuleName and TemplateModuleName intentionally omitted
        };

        var json = JsonSerializer.Serialize(original, Options);

        json.ShouldNotContain("ModuleName");

        var restored = JsonSerializer.Deserialize<NazcaCodeOverride>(json, Options);
        restored!.ModuleName.ShouldBeNull();
        restored.TemplateModuleName.ShouldBeNull();
    }

    [Fact]
    public void DictionaryRoundTrip_MultipleInstances_AllPreserved()
    {
        var dict = new Dictionary<string, NazcaCodeOverride>
        {
            ["mmi_1"] = new NazcaCodeOverride
            {
                FunctionName = "custom_mmi",
                TemplateFunctionName = "ebeam_mmi1x2",
                TemplateFunctionParameters = ""
            },
            ["wg_2"] = new NazcaCodeOverride
            {
                FunctionName = "custom_wg",
                FunctionParameters = "width=0.5",
                TemplateFunctionName = "ebeam_wg",
                TemplateFunctionParameters = "width=0.45"
            }
        };

        var json = JsonSerializer.Serialize(dict, Options);
        var restored = JsonSerializer.Deserialize<Dictionary<string, NazcaCodeOverride>>(json, Options);

        restored.ShouldNotBeNull();
        restored!.Count.ShouldBe(2);
        restored["mmi_1"].FunctionName.ShouldBe("custom_mmi");
        restored["wg_2"].FunctionParameters.ShouldBe("width=0.5");
    }

    [Fact]
    public void OldLunFile_MissingNazcaOverrides_DeserializesAsNull()
    {
        // Simulate a .lun file that has no NazcaOverrides section
        const string json = """{ "Components": [], "Connections": [] }""";
        var data = JsonSerializer.Deserialize<LegacyDesignFileStub>(json);

        data.ShouldNotBeNull();
        data!.NazcaOverrides.ShouldBeNull();
    }

    /// <summary>Minimal DTO to test backward-compatible deserialization of old .lun files.</summary>
    private class LegacyDesignFileStub
    {
        public Dictionary<string, NazcaCodeOverride>? NazcaOverrides { get; set; }
    }
}
