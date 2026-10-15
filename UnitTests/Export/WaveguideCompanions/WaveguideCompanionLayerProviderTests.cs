using CAP.Avalonia.Services.WaveguideCompanions;
using CAP_Core.Components.Core;
using CAP_Core.Routing.WaveguideCompanions;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Shouldly;
using Xunit;

namespace UnitTests.Export.WaveguideCompanions;

/// <summary>
/// The shared canvas / export provider resolves a waveguide's bands from its endpoint
/// components' PDK and falls back to the design-wide (active process) spec otherwise.
/// </summary>
public class WaveguideCompanionLayerProviderTests
{
    [Fact]
    public void DemoComponentEndpoints_ResolveTheDemoTrench()
    {
        var templates = TestPdkLoader.LoadFromPdk("demo-pdk.json");
        var drafts = new[] { LoadDemoDraft() };
        var provider = new WaveguideCompanionLayerProvider(() => templates, () => drafts, () => WaveguideCompanionSpec.Empty);
        var mmi = templates.First(t => t.NazcaFunctionName == "demo.mmi1x2_sh");

        var spec = provider.Resolve(PinOn(mmi.NazcaFunctionName!), PinOn(mmi.NazcaFunctionName!));

        spec.Layers.ShouldHaveSingleItem().GdsLayer.ShouldBe(2);
    }

    [Fact]
    public void UnknownEndpoints_FallBackToTheDesignWideSpec()
    {
        var designWide = new WaveguideCompanionSpec(new[]
        {
            new WaveguideCompanionLayer("K", 40, 0, WaveguideCompanionRole.KeepOut, 1.0, null),
        });
        var provider = new WaveguideCompanionLayerProvider(
            () => TestPdkLoader.LoadFromPdk("demo-pdk.json"), () => new[] { LoadDemoDraft() }, () => designWide);

        provider.Resolve(PinOn("not_a_library_cell"), null).ShouldBeSameAs(designWide);
    }

    private static PhysicalPin PinOn(string nazcaFunction)
    {
        var component = TestComponentFactory.CreateBasicComponent();
        component.NazcaFunctionName = nazcaFunction;
        var pin = new PhysicalPin { Name = "p", ParentComponent = component };
        component.PhysicalPins.Add(pin);
        return pin;
    }

    private static CAP_DataAccess.Components.ComponentDraftMapper.DTOs.PdkDraft LoadDemoDraft() =>
        new PdkLoader().LoadFromFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PDKs", "demo-pdk.json"));
}
