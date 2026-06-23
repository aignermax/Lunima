// UnitTests/Services/ComponentGeometryKeyTests.cs
using CAP.Avalonia.Services;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests;
using Xunit;

namespace UnitTests.Services;

public class ComponentGeometryKeyTests
{
    private static Component Wg(string module, string function, string parameters)
    {
        var c = TestComponentFactory.CreateStraightWaveGuide();
        c.NazcaModuleName = module;
        c.NazcaFunctionName = function;
        c.NazcaFunctionParameters = parameters;
        return c;
    }

    [Fact]
    public void SameModuleFunctionParameters_SameKey()
    {
        var a = ComponentGeometryKey.For(Wg("siepic", "ebeam_dc", "Lc=5"), _ => null);
        var b = ComponentGeometryKey.For(Wg("siepic", "ebeam_dc", "Lc=5"), _ => null);
        a.ShouldBe(b);
    }

    [Fact]
    public void DifferentParameters_DifferentKey()
    {
        var a = ComponentGeometryKey.For(Wg("siepic", "ebeam_dc", "Lc=5"), _ => null);
        var b = ComponentGeometryKey.For(Wg("siepic", "ebeam_dc", "Lc=9"), _ => null);
        a.ShouldNotBe(b);
    }

    [Fact]
    public void RawCodeOverride_UsesCodeHash_IndependentOfFunction()
    {
        var comp = Wg("siepic", "ebeam_dc", "Lc=5");
        var withOverride = ComponentGeometryKey.For(comp, _ => "import nazca; def component(): ...");
        var noOverride = ComponentGeometryKey.For(comp, _ => null);
        withOverride.ShouldNotBe(noOverride);
        var other = Wg("other", "different_fn", "");
        ComponentGeometryKey.For(other, _ => "import nazca; def component(): ...")
            .ShouldBe(withOverride);
    }

    [Fact]
    public void Prefixed_RawVsGeo_NeverCollide()
    {
        ComponentGeometryKey.For(Wg("m", "f", "p"), _ => null).ShouldStartWith("geo:");
        ComponentGeometryKey.For(Wg("m", "f", "p"), _ => "x").ShouldStartWith("raw:");
    }
}
