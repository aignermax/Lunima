using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.PdkOffset;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;

namespace UnitTests.PdkOffset;

/// <summary>Preview JSON parsing and default pin fields.</summary>
[Collection("NazcaComponentPreviewService")]
public class NazcaComponentPreviewParsingTests
{
    // Keep localized status assertions independent of the developer or CI language.
    public NazcaComponentPreviewParsingTests() =>
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

    [Fact]
    public void ParseOutput_EmptyString_ReturnsFailure()
    {
        var result = NazcaComponentPreviewService.ParseOutput("");
        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void ParseOutput_MalformedJson_ReturnsFailure()
    {
        var result = NazcaComponentPreviewService.ParseOutput("{not json");
        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void ParseOutput_ScriptReportedFailure_PropagatesError()
    {
        var json = "{\"success\": false, \"error\": \"function not found\"}";
        var result = NazcaComponentPreviewService.ParseOutput(json);
        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("function not found");
    }

    [Fact]
    public void ParseOutput_ValidPayload_PopulatesBBoxPolygonsAndPins()
    {
        const string json =
            "{\"success\": true, " +
            "\"bbox\": {\"xmin\": -5.0, \"ymin\": -2.0, \"xmax\": 30.0, \"ymax\": 10.0}, " +
            "\"polygons\": [{\"layer\": 1, \"vertices\": [[0,0],[1,0],[1,1],[0,1]]}], " +
            "\"pins\": [{\"name\": \"a0\", \"x\": 0.0, \"y\": 4.0, \"angle\": 180.0, " +
            "\"stubX1\": -3.0, \"stubY1\": 4.0}]}";

        var result = NazcaComponentPreviewService.ParseOutput(json);

        result.Success.ShouldBeTrue();
        result.XMin.ShouldBe(-5.0);
        result.YMax.ShouldBe(10.0);
        result.Polygons.Count.ShouldBe(1);
        result.Polygons[0].Layer.ShouldBe(1);
        result.Polygons[0].Vertices.Count.ShouldBe(4);
        result.Pins.Count.ShouldBe(1);
        result.Pins[0].Name.ShouldBe("a0");
        result.Pins[0].StubX1.ShouldBe(-3.0);
    }

    [Fact]
    public void ParseOutput_PinWithoutStubFields_DefaultsToZero()
    {
        // Defensive: missing optional fields must not throw — older preview
        // scripts may not emit stubX1/stubY1.
        const string json =
            "{\"success\": true, " +
            "\"bbox\": {\"xmin\": 0, \"ymin\": 0, \"xmax\": 1, \"ymax\": 1}, " +
            "\"polygons\": [], " +
            "\"pins\": [{\"name\": \"p\", \"x\": 0.0, \"y\": 0.0, \"angle\": 0.0}]}";

        var result = NazcaComponentPreviewService.ParseOutput(json);

        result.Success.ShouldBeTrue();
        result.Pins.Count.ShouldBe(1);
        result.Pins[0].StubX1.ShouldBe(0.0);
        result.Pins[0].StubY1.ShouldBe(0.0);
    }
}
