using System.IO;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Shouldly;

namespace UnitTests.Components;

/// <summary>
/// Verifies the editing-tolerant loader path used by the PDK Offset Editor:
/// it must accept PDKs with null NazcaOriginOffsets (the tool fixes them),
/// while the strict main path must keep rejecting them.
/// </summary>
public class PdkLoaderOffsetEditingTests
{
    private const string PdkWithNullOffsets = @"{
        ""fileFormatVersion"": 1,
        ""name"": ""Uncalibrated PDK"",
        ""components"": [
            {
                ""name"": ""Raw Waveguide"",
                ""category"": ""Waveguides"",
                ""nazcaFunction"": ""raw.wg"",
                ""widthMicrometers"": 100,
                ""heightMicrometers"": 5,
                ""pins"": [
                    { ""name"": ""a0"", ""offsetXMicrometers"": 0,   ""offsetYMicrometers"": 2.5 },
                    { ""name"": ""b0"", ""offsetXMicrometers"": 100, ""offsetYMicrometers"": 2.5 }
                ]
            }
        ]
    }";

    [Fact]
    public void LoadFromFileForEditing_WhenOffsetsNull_ReturnsPdkInsteadOfThrowing()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, PdkWithNullOffsets);
            var loader = new PdkLoader();

            var pdk = loader.LoadFromFileForEditing(tempFile);

            pdk.ShouldNotBeNull();
            pdk.Components.Count.ShouldBe(1);
            pdk.Components[0].NazcaOriginOffsetX.ShouldBeNull();
            pdk.Components[0].NazcaOriginOffsetY.ShouldBeNull();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadFromFile_WhenOffsetsNull_StillThrowsValidationException()
    {
        // Regression guard: the strict path (used by simulation and GDS export)
        // must keep rejecting PDKs with missing offsets — the editing-tolerant
        // method must not have relaxed the strict one by accident.
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, PdkWithNullOffsets);
            var loader = new PdkLoader();

            var ex = Should.Throw<PdkValidationException>(() => loader.LoadFromFile(tempFile));
            ex.Errors.ShouldContain(e => e.Contains("nazcaOriginOffsetX"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadFromFileForEditing_StillRejectsStructuralErrors()
    {
        // Editing-tolerant must only relax the offset check — pins, dimensions,
        // names still matter (otherwise the editor would silently load garbage).
        const string brokenPdk = @"{
            ""fileFormatVersion"": 1,
            ""name"": ""Broken PDK"",
            ""components"": [
                { ""name"": ""NoPins"", ""nazcaFunction"": ""x"", ""widthMicrometers"": 10, ""heightMicrometers"": 5, ""pins"": [] }
            ]
        }";
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, brokenPdk);
            var loader = new PdkLoader();

            var ex = Should.Throw<PdkValidationException>(() => loader.LoadFromFileForEditing(tempFile));
            ex.Errors.ShouldContain(e => e.Contains("pin"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
