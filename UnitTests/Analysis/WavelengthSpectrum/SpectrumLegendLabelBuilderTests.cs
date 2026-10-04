using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Analysis.WavelengthSpectrum;

/// <summary>
/// Tests for the spectrum legend label builder (#1374): pins of same-type
/// components must get distinct legend labels, a user-set
/// <see cref="Component.HumanReadableName"/> is shown verbatim, and
/// single-instance designs keep their plain "Component.pin" labels.
/// </summary>
public class SpectrumLegendLabelBuilderTests
{
    [Fact]
    public void BuildPinNameMap_SingleComponent_KeepsPlainTypeLabel()
    {
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        component.Identifier = "Grating Coupler TE 1550";

        var map = Build(component);

        map.Values.Distinct().ShouldBe(
            new[] { "Grating Coupler TE 1550.in", "Grating Coupler TE 1550.out" },
            ignoreOrder: true);
    }

    [Fact]
    public void BuildPinNameMap_DuplicateTypeNames_GetDistinctLabels()
    {
        var first = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        first.Identifier = "Grating Coupler TE 1550";
        var second = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        second.Identifier = "Grating Coupler TE 1550";

        var map = Build(first, second);

        map.Values.Distinct().Count().ShouldBe(4,
            "every legend entry must be unique — no more 4× 'Grating Coupler TE 1550.port 2'");
        LabelsOf(first, map).ShouldAllBe(l => l.StartsWith("Grating Coupler TE 1550 #1."));
        LabelsOf(second, map).ShouldAllBe(l => l.StartsWith("Grating Coupler TE 1550 #2."));
    }

    [Fact]
    public void BuildPinNameMap_HumanReadableName_WinsAndIsShownAsIs()
    {
        var named = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        named.Identifier = "gc_through";
        named.HumanReadableName = "Through";
        var typeNamed = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        typeNamed.Identifier = "Through";

        var map = Build(named, typeNamed);

        map.Values.ShouldContain("Through.out",
            "a user-set HumanReadableName is shown verbatim");
        map.Values.ShouldContain("Through #2.out",
            "the colliding type-named instance gets the suffix instead");
        map.Values.ShouldNotContain("Through #1.out");
    }

    [Fact]
    public void BuildPinNameMap_MapsBothFlowDirectionsToSameLabel()
    {
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        component.Identifier = "GC";
        var pin = component.PhysicalPins[0];

        var map = Build(component);

        map[pin.LogicalPin!.IDInFlow].ShouldBe("GC.in");
        map[pin.LogicalPin.IDOutFlow].ShouldBe("GC.in");
    }

    [Fact]
    public void ComposeCurveLabel_SingleInput_NamesBothEnds()
    {
        SpectrumLegendLabelBuilder.ComposeCurveLabel("In", "Drop.port 2")
            .ShouldBe("In → Drop.port 2");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ComposeCurveLabel_NoAttributableInput_KeepsOutputLabel(string? inputLabel)
    {
        SpectrumLegendLabelBuilder.ComposeCurveLabel(inputLabel, "Drop.port 2")
            .ShouldBe("Drop.port 2");
    }

    private static IEnumerable<string> LabelsOf(Component component, Dictionary<Guid, string> map) =>
        component.PhysicalPins.Select(p => map[p.LogicalPin!.IDInFlow]);

    private static Dictionary<Guid, string> Build(params Component[] components) =>
        SpectrumLegendLabelBuilder.BuildPinNameMap(
            SpectrumLegendLabelBuilder.BuildDisplayNames(components));
}
