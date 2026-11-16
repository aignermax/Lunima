using System.IO;
using System.Text.Json.Nodes;
using CAP.Avalonia.Services.AddCustomComponent;
using CAP_Core.LightCalculation.Photodetection;
using CAP_Core.Analysis.EyeDiagram;
using CAP_Core.LightCalculation.TimeDomainSimulation.CompactModels.Models;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation.Photodetection;

public class PhotodetectorPersistenceTests
{
    private const string Json = """
        {"name":"Receiver test", "process":{"name":"Test process", "photodetector":{
          "responsivityAmpsPerWatt":0.9, "darkCurrentNa":12, "sensitivityDbm":-19,
          "bitRateHz":10000000000, "ber":1e-12, "wavelengthNm":1550,
          "provenance":"foundry", "source":"Synthetic test qualification document, 1550 nm, -1 V"
        }}, "components":[]}
        """;

    [Theory]
    [InlineData(null, 0.9, "foundry")]
    [InlineData(0.7, 0.7, "custom")]
    public void PdkResolutionFeedsAllConsumers(double? responsivityOverride, double expected, string provenance)
    {
        var pdk = new PdkLoader().LoadFromJson(Json);
        var output = new PdkComponentDraft
        {
            CompactModelParameters = responsivityOverride.HasValue ? new()
            {
                [PhotodiodeRcModel.ResponsivityKey] = responsivityOverride.Value
            } : null
        };
        var resolved = ProcessPhotodetectorResolver.Resolve(pdk, output);
        resolved.Provenance.ShouldBe(provenance);
        resolved.ResponsivityAmpsPerWatt.ShouldBe(expected);
        var model = new PhotodiodeRcModel(detector: resolved);
        var noise = new NoiseModel { Detector = resolved };
        model.ResponsivityAmpsPerWatt.ShouldBe(noise.ResponsivityAPerW);
        OutputPhotocurrent.FromDbm(Guid.NewGuid(), resolved, 0).PhotocurrentAmps!.Value
            .ShouldBe(noise.PhotocurrentAmps(0.001), 1e-15);
    }

    [Fact]
    public void MissingProcessFallsBackWithoutPersistingInventedData()
    {
        var pdk = new PdkLoader().LoadFromJson("""{"name":"Legacy", "components":[]}""");
        ProcessPhotodetectorResolver.Resolve(pdk).Provenance.ShouldBe("generic");
        pdk.Process.ShouldBeNull();
    }

    [Fact]
    public void FoundryProcessOverridesGenericAndRoundTripsThroughFileAndFork()
    {
        var pdk = new PdkLoader().LoadFromJson(Json);
        var detector = ProcessPhotodetectorResolver.Resolve(pdk);
        detector.Provenance.ShouldBe("foundry");
        detector.ResponsivityAmpsPerWatt.ShouldBe(0.9);
        detector.DarkCurrentNa.ShouldBe(12);
        var path = Path.Combine(Path.GetTempPath(), $"detector-{Guid.NewGuid()}.json");
        try
        {
            pdk.Process = ProcessDefinitionCloner.Clone(pdk.Process!);
            new PdkJsonSaver().SaveToFile(pdk, path);
            var reloaded = ProcessPhotodetectorResolver.Resolve(new PdkLoader().LoadFromFile(path));
            reloaded.Source.ShouldBe(detector.Source);
            reloaded.Provenance.ShouldBe(detector.Provenance);
            reloaded.ResponsivityAmpsPerWatt.ShouldBe(detector.ResponsivityAmpsPerWatt);
            reloaded.DarkCurrentNa.ShouldBe(detector.DarkCurrentNa);
            reloaded.SensitivityDbm.ShouldBe(detector.SensitivityDbm);
            reloaded.Ber.ShouldBe(detector.Ber);
            reloaded.BitRateHz.ShouldBe(detector.BitRateHz);
            reloaded.WavelengthNm.ShouldBe(detector.WavelengthNm);
            ProcessPhotodetectorResolver.Resolve(new PdkLoader().LoadFromFileTolerant(path).Pdk)
                .Provenance.ShouldBe("foundry");
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("demo-pdk.json")]
    [InlineData("siepic-ebeam-pdk.json")]
    [InlineData("cornerstone-sin-pdk.json")]
    [InlineData("tools-pdk.json")]
    public void UnchangedBundledPdksUseGenericFallback(string filename)
    {
        var pdk = new PdkLoader().LoadFromFile(Path.Combine(AppContext.BaseDirectory, "PDKs", filename));
        pdk.Process?.Photodetector.ShouldBeNull();
        ProcessPhotodetectorResolver.Resolve(pdk).ShouldBeSameAs(PhotodetectorResolver.Generic);
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("literature")]
    [InlineData("custom")]
    public void JsonPresenceDoesNotPromoteProvenance(string provenance)
    {
        var pdk = new PdkLoader().LoadFromJson(Json.Replace("\"foundry\"", $"\"{provenance}\""));
        ProcessPhotodetectorResolver.Resolve(pdk).Provenance.ShouldBe(provenance);
    }

    [Theory]
    [InlineData("responsivityAmpsPerWatt")]
    [InlineData("darkCurrentNa")]
    [InlineData("sensitivityDbm")]
    [InlineData("bitRateHz")]
    [InlineData("ber")]
    [InlineData("wavelengthNm")]
    [InlineData("provenance")]
    [InlineData("source")]
    public void IncompleteSectionIsRejected(string field)
    {
        var json = JsonNode.Parse(Json)!;
        json["process"]!["photodetector"]!.AsObject().Remove(field);
        Should.Throw<ArgumentException>(() => new PdkLoader().LoadFromJson(json.ToJsonString()));
    }

    [Fact]
    public void UnqualifiedFoundryAndUnknownProvenanceAreRejected()
    {
        Should.Throw<ArgumentException>(() => new PhotodetectorDescription(1, 6, -20, 10e9, 1e-12, 1550, "foundry", " "));
        Should.Throw<ArgumentException>(() => new PhotodetectorDescription(1, 6, -20, 10e9, 1e-12, 1550, "guessed", "test"));
    }
}
