using System.Text.Json;
using CAP_Core.Components.Process;
using CAP_Core.Routing.WaveguideCompanions;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// PDK JSON → <see cref="WaveguideCompanionSpec"/>: parsing, validation (malformed entries are
/// skipped, never guessed), the shipped per-process defaults, and per-connection resolution.
/// </summary>
public class WaveguideCompanionSpecResolverTests
{
    [Fact]
    public void Json_ParsesCompanionLayersWithRoleAndOffset()
    {
        const string json = """
            { "name": "P", "waveguideCompanionLayers": [
              { "name": "TRENCH", "layer": 2, "datatype": 0, "role": "Trench", "offsetUm": 8.0 },
              { "name": "KEEP", "layer": 40, "datatype": 1, "role": "KeepOut", "widthUm": 12.5 } ] }
            """;

        var process = JsonSerializer.Deserialize<ProcessDefinition>(json)!;
        var spec = WaveguideCompanionSpecResolver.FromProcess(process);

        spec.Layers.Count.ShouldBe(2);
        spec.Layers[0].ShouldBe(new WaveguideCompanionLayer("TRENCH", 2, 0, WaveguideCompanionRole.Trench, 8.0, null));
        spec.Layers[1].Role.ShouldBe(WaveguideCompanionRole.KeepOut);
        spec.Layers[1].TotalWidthMicrometers.ShouldBe(12.5);
        spec.KeepOutOffsetFor(0.5).ShouldBe(6.0, 1e-9);
    }

    [Fact]
    public void ProcessWithoutCompanionLayers_SerializesWithoutTheField()
    {
        var json = JsonSerializer.Serialize(new ProcessDefinition { Name = "P" });

        json.ShouldNotContain("waveguideCompanionLayers");
        WaveguideCompanionSpecResolver.FromProcess(new ProcessDefinition()).IsEmpty.ShouldBeTrue();
        WaveguideCompanionSpecResolver.FromProcess(null).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void MalformedEntries_AreSkipped()
    {
        var process = new ProcessDefinition
        {
            WaveguideCompanionLayers = new List<ProcessCompanionLayer>
            {
                new() { Name = "neither", Layer = 2 },
                new() { Name = "both", Layer = 3, OffsetUm = 1, WidthUm = 5 },
                new() { Name = "negative", Layer = 4, OffsetUm = -1 },
                new() { Name = "ok", Layer = 5, OffsetUm = 2 },
            },
        };

        WaveguideCompanionSpecResolver.FromProcess(process).Layers
            .Select(l => l.Name).ShouldBe(new[] { "ok" });
    }

    [Fact]
    public void ShippedDemoProcess_DrawsTheDemofabShallowTrench()
    {
        var spec = WaveguideCompanionSpecResolver.FromProcess(LoadBundled("demo-pdk.json").Process);

        var trench = spec.Layers.ShouldHaveSingleItem();
        trench.GdsLayer.ShouldBe(2);
        trench.GdsDataType.ShouldBe(0);
        trench.Role.ShouldBe(WaveguideCompanionRole.Trench);
        trench.OffsetEachSideMicrometers.ShouldBe(8.0);
        spec.KeepOutOffsetFor(0.45).ShouldBe(0.0, "the trench must not tighten the spacing check");
    }

    [Theory]
    [InlineData("cornerstone-sin-pdk.json")]
    [InlineData("siepic-ebeam-pdk.json")]
    public void ShippedCoreOnlyProcesses_DeclareNoCompanionLayers(string pdkFile)
    {
        WaveguideCompanionSpecResolver.FromProcess(LoadBundled(pdkFile).Process).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void EndpointResolution_UnknownPdks_HaveNoOpinion()
    {
        var drafts = new List<PdkDraft> { Draft("A", 2) };

        WaveguideCompanionSpecResolver.ResolveForEndpointPdkNames(null, "unknown", drafts).ShouldBeNull();
        WaveguideCompanionSpecResolver.ResolveForEndpointPdkNames("A", null, null).ShouldBeNull();
    }

    [Fact]
    public void EndpointResolution_CrossProcessRoute_CarriesBothProcessesBands()
    {
        var drafts = new List<PdkDraft> { Draft("A", 2), Draft("B", 7) };

        var spec = WaveguideCompanionSpecResolver.ResolveForEndpointPdkNames("A", "B", drafts)!;

        spec.Layers.Select(l => l.GdsLayer).ShouldBe(new[] { 2, 7 });
        WaveguideCompanionSpecResolver.ResolveForEndpointPdkNames("A", "A", drafts)!
            .Layers.ShouldHaveSingleItem().GdsLayer.ShouldBe(2);
    }

    [Fact]
    public void ActiveProcess_FirstMemberWithBandsSupplies_PlaygroundHasNone()
    {
        var drafts = new List<PdkDraft> { new() { Name = "Plain", Process = new ProcessDefinition() }, Draft("A", 2) };
        var active = new ActiveProcessSelection("P", null, new List<string> { "Plain", "A" }, IsPlayground: false);

        WaveguideCompanionSpecResolver.FromActiveProcess(active, drafts).Layers
            .ShouldHaveSingleItem().GdsLayer.ShouldBe(2);
        WaveguideCompanionSpecResolver.FromActiveProcess(ActiveProcessSelection.Playground(), drafts)
            .IsEmpty.ShouldBeTrue();
        WaveguideCompanionSpecResolver.FromActiveProcess(null, drafts).IsEmpty.ShouldBeTrue();
    }

    private static PdkDraft Draft(string name, int layer) => new()
    {
        Name = name,
        Process = new ProcessDefinition
        {
            Name = name + " process",
            WaveguideCompanionLayers = new List<ProcessCompanionLayer>
            {
                new() { Name = "T" + layer, Layer = layer, Role = WaveguideCompanionRole.Trench, OffsetUm = 1.0 },
            },
        },
    };

    private static PdkDraft LoadBundled(string fileName) =>
        new PdkLoader().LoadFromFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PDKs", fileName));
}
