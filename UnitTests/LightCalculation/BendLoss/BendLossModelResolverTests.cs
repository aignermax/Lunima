using System.Text.Json;
using CAP_Core.LightCalculation.BendLoss;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation.BendLoss;

/// <summary>
/// Per-process bend-loss defaults: the waveguide family is inferred from the PDK process (core
/// material, etch depth), an explicit <c>bendLoss</c> block overrides it field by field, and
/// the shipped PDKs resolve to their physical platforms.
/// </summary>
public class BendLossModelResolverTests
{
    [Fact]
    public void NoProcess_ResolvesToNull_SoTheGenericDefaultApplies()
    {
        BendLossModelResolver.Resolve(null).ShouldBeNull();
    }

    [Theory]
    [InlineData("Si", 220.0, 5.0, WaveguideFamily.SoiStrip)]
    [InlineData("Si3N4", 300.0, 30.0, WaveguideFamily.SiliconNitride)]
    [InlineData("SiN", 400.0, 50.0, WaveguideFamily.SiliconNitride)]
    [InlineData("InGaAsP", 500.0, 10.0, WaveguideFamily.InpDeepEtch)]
    [InlineData("InGaAsP", 500.0, 150.0, WaveguideFamily.InpShallowRib)]
    [InlineData("LiNbO3", 600.0, 80.0, WaveguideFamily.Generic)]
    public void Family_IsInferredFromTheCoreMaterial(string core, double thickness, double minRadius, WaveguideFamily expected)
    {
        var model = BendLossModelResolver.Resolve(Process(core, thickness, minRadius));

        model.ShouldNotBeNull();
        model.Family.ShouldBe(expected);
    }

    [Fact]
    public void CoreThickness220WithoutMaterials_IsSoi()
    {
        var process = Process(core: null, thickness: 220, minRadius: 5);

        BendLossModelResolver.Resolve(process)!.Family.ShouldBe(WaveguideFamily.SoiStrip);
    }

    [Fact]
    public void ExplicitCoefficients_OverrideThePreset()
    {
        var process = Process("Si3N4", 300, 30);
        process.Xsections[0].BendLoss = new ProcessBendLossDraft
        {
            RadiationLossDbPer90 = 0.2,
            RadiationReferenceRadiusUm = 40,
            Source = "foundry measurement",
        };

        var model = BendLossModelResolver.Resolve(process)!;

        model.Family.ShouldBe(WaveguideFamily.Custom);
        model.RadiationLossDbPer90AtReference.ShouldBe(0.2);
        model.RadiationReferenceRadiusMicrometers.ShouldBe(40);
        model.RadiationDecayLengthMicrometers.ShouldBe(BendLossPresets.SiliconNitride.RadiationDecayLengthMicrometers,
            "omitted fields keep the family default");
        model.Source.ShouldBe("foundry measurement");
    }

    [Fact]
    public void ExplicitFamily_WinsOverInference_AndInvalidValuesAreIgnored()
    {
        var process = Process("Si", 220, 5);
        process.Xsections[0].BendLoss = new ProcessBendLossDraft { Family = "InpShallowRib", RadiationDecayLengthUm = -3 };

        var model = BendLossModelResolver.Resolve(process)!;

        model.Family.ShouldBe(WaveguideFamily.InpShallowRib);
        model.RadiationDecayLengthMicrometers.ShouldBe(BendLossPresets.InpShallowRib.RadiationDecayLengthMicrometers);
    }

    [Fact]
    public void BendLossBlock_RoundTripsThroughJson_AndIsOmittedWhenAbsent()
    {
        var xsection = new ProcessXsection { Name = "strip", WidthUm = 0.5, MinRadiusUm = 5 };
        JsonSerializer.Serialize(xsection).ShouldNotContain("bendLoss");

        xsection.BendLoss = new ProcessBendLossDraft { Family = "SoiStrip", JunctionLossDb = 0.01 };
        var json = JsonSerializer.Serialize(xsection);
        var back = JsonSerializer.Deserialize<ProcessXsection>(json)!;

        back.BendLoss!.Family.ShouldBe("SoiStrip");
        back.BendLoss.JunctionLossDb.ShouldBe(0.01);
        json.ShouldNotContain("radiationLossDbPer90", Case.Sensitive, "null coefficients are not written");
    }

    [Theory]
    [InlineData("siepic-ebeam-pdk.json", WaveguideFamily.SoiStrip)]
    [InlineData("demo-pdk.json", WaveguideFamily.SoiStrip)]
    [InlineData("cornerstone-sin-pdk.json", WaveguideFamily.SiliconNitride)]
    public void BundledPdks_ResolveToTheirPlatform(string fileName, WaveguideFamily expected)
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PDKs", fileName);
        var pdk = new PdkLoader().LoadFromFile(path);

        BendLossModelResolver.Resolve(pdk.Process)!.Family.ShouldBe(expected);
    }

    private static ProcessDefinition Process(string? core, double thickness, double minRadius)
    {
        var process = new ProcessDefinition { Name = "test", CoreThicknessNm = thickness };
        process.Xsections.Add(new ProcessXsection
        {
            Name = "wg", Kind = XsectionKind.Optical, WidthUm = 1, MinRadiusUm = minRadius,
        });
        if (core != null)
            process.Materials.Add(new ProcessMaterial { Name = core, Role = "core" });
        return process;
    }
}
